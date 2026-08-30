using AuditNode.Application.DTOs;
using AuditNode.Application.Exceptions;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;
using AuditNode.API.Security;
using Microsoft.AspNetCore.DataProtection;

namespace AuditNode.Tests.Repositories;

public sealed class GlobalCatalogRepositoryTests
{
    private static readonly DateTime Now = new(2026, 8, 29, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Mine_returns_only_owned_resources_and_fails_closed_for_legacy_null_owner()
    {
        await using var context = Context();
        var mine = Server("me", "same");
        var other = Server("other", "same");
        var legacy = Server(null, "legacy");
        context.AddRange(mine.Datacenter!, other.Datacenter!, legacy.Datacenter!, mine, other, legacy);
        await context.SaveChangesAsync();

        var page = await Repository(context).GetServersAsync("me", Query(CatalogView.Mine), Now);

        page.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        page.Items[0].EffectivePermission.Should().Be(LabelEffectivePermission.Owner);
        page.Items[0].OwnerUserId.Should().Be("me");
        page.Items[0].Capabilities.CanManageGrants.Should().BeTrue();
    }

    [Fact]
    public async Task Shared_deduplicates_overlapping_grants_and_resolves_editor_permission()
    {
        await using var context = Context();
        var server = Server("owner", "shared");
        var viewer = BusinessLabel("owner", "viewer");
        var editor = BusinessLabel("owner", "editor");
        context.AddRange(server.Datacenter!, server, viewer, editor);
        context.ServerLabels.AddRange(Link(server, viewer), Link(server, editor));
        context.LabelGrants.AddRange(
            Grant(viewer, "reader", LabelGrantPermissions.Viewer),
            Grant(editor, "reader", LabelGrantPermissions.Editor));
        await context.SaveChangesAsync();

        var page = await Repository(context).GetServersAsync("reader", Query(CatalogView.Shared), Now);

        var item = page.Items.Should().ContainSingle().Subject;
        item.EffectivePermission.Should().Be(LabelEffectivePermission.Editor);
        item.SharedLabelIds.Should().BeEquivalentTo([viewer.Id, editor.Id]);
        item.Capabilities.CanEditProperties.Should().BeTrue();
        item.Capabilities.CanChangeLabels.Should().BeFalse();
    }

    [Fact]
    public async Task Cursor_uses_id_as_tie_breaker_for_duplicate_sort_values()
    {
        await using var context = Context();
        var first = Server("me", "duplicate", Guid.Parse("10000000-0000-0000-0000-000000000000"));
        var second = Server("me", "duplicate", Guid.Parse("20000000-0000-0000-0000-000000000000"));
        context.AddRange(first.Datacenter!, second.Datacenter!, first, second);
        await context.SaveChangesAsync();

        var repository = Repository(context);
        var page1 = await repository.GetServersAsync("me", Query(CatalogView.Mine, 1), Now);
        var page2 = await repository.GetServersAsync("me", Query(CatalogView.Mine, 1, page1.NextCursor), Now);

        page1.Items.Should().ContainSingle().Which.Id.Should().Be(first.Id);
        page2.Items.Should().ContainSingle().Which.Id.Should().Be(second.Id);
        page2.Items.Select(item => item.Id).Should().NotIntersectWith(page1.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Cursor_remains_safe_when_rows_are_inserted_or_deleted_between_pages()
    {
        await using var context = Context();
        var first = Server("me", "b");
        var second = Server("me", "c");
        var third = Server("me", "d");
        context.AddRange(first.Datacenter!, second.Datacenter!, third.Datacenter!, first, second, third);
        await context.SaveChangesAsync();
        var repository = Repository(context);

        var page1 = await repository.GetServersAsync("me", Query(CatalogView.Mine, 1), Now);
        context.Servers.Remove(first);
        var insertedBeforeCursor = Server("me", "a");
        context.AddRange(insertedBeforeCursor.Datacenter!, insertedBeforeCursor);
        await context.SaveChangesAsync();
        var page2 = await repository.GetServersAsync("me", Query(CatalogView.Mine, 2, page1.NextCursor), Now);

        page2.Items.Select(item => item.Id).Should().Equal(second.Id, third.Id);
    }

    [Fact]
    public async Task Cursor_is_rejected_when_malformed_or_bound_to_another_endpoint_or_view()
    {
        await using var context = Context();
        var server = Server("me", "server");
        var secondServer = Server("me", "server-two");
        var app = new AuditNode.Domain.Entities.Application
        {
            Id = Guid.NewGuid(), WorkspaceId = server.WorkspaceId, OwnerUserId = "me", AppCode = "APP", AppName = "App"
        };
        context.AddRange(server.Datacenter!, secondServer.Datacenter!, server, secondServer, app);
        await context.SaveChangesAsync();
        var repository = Repository(context);
        var serverPage = await repository.GetServersAsync("me", Query(CatalogView.Mine, 1), Now);

        var malformed = () => repository.GetServersAsync("me", Query(CatalogView.Mine, 1, "not-a-cursor"), Now);
        var wrongEndpoint = () => repository.GetApplicationsAsync("me", Query(CatalogView.Mine, 1, serverPage.NextCursor), Now);
        var wrongView = () => repository.GetServersAsync("me", Query(CatalogView.Shared, 1, serverPage.NextCursor), Now);

        await malformed.Should().ThrowAsync<CatalogQueryValidationException>();
        await wrongEndpoint.Should().ThrowAsync<CatalogQueryValidationException>();
        await wrongView.Should().ThrowAsync<CatalogQueryValidationException>();
    }

    [Fact]
    public async Task Shared_datacenters_are_distinct_and_always_read_only()
    {
        await using var context = Context();
        var datacenter = Datacenter("owner", "dc");
        var first = Server("owner", "one", datacenter: datacenter);
        var second = Server("owner", "two", datacenter: datacenter);
        var label = BusinessLabel("owner", "shared");
        context.AddRange(datacenter, first, second, label);
        context.ServerLabels.AddRange(Link(first, label), Link(second, label));
        context.LabelGrants.Add(Grant(label, "reader", LabelGrantPermissions.Editor));
        await context.SaveChangesAsync();

        var page = await Repository(context).GetDatacentersAsync("reader", Query(CatalogView.Shared), Now);

        var item = page.Items.Should().ContainSingle().Subject;
        item.EffectivePermission.Should().Be(LabelEffectivePermission.Viewer);
        item.Capabilities.CanRead.Should().BeTrue();
        item.Capabilities.CanEditProperties.Should().BeFalse();
        item.SharedLabelIds.Should().ContainSingle().Which.Should().Be(label.Id);
    }

    [Fact]
    public async Task Shared_search_is_authorized_before_pagination_and_deduplicates_grants()
    {
        await using var context = Context();
        var visible = Server("owner", "match-visible");
        var hidden = Server("owner", "match-hidden");
        var firstLabel = BusinessLabel("owner", "one");
        var secondLabel = BusinessLabel("owner", "two");
        context.AddRange(visible.Datacenter!, hidden.Datacenter!, visible, hidden, firstLabel, secondLabel);
        context.ServerLabels.AddRange(Link(visible, firstLabel), Link(visible, secondLabel));
        context.LabelGrants.AddRange(Grant(firstLabel, "reader", LabelGrantPermissions.Viewer), Grant(secondLabel, "reader", LabelGrantPermissions.Editor));
        await context.SaveChangesAsync();

        var page = await Repository(context).SearchAsync("reader", "match", Query(CatalogView.Shared, 1), Now);

        var item = page.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(visible.Id);
        item.EffectivePermission.Should().Be(LabelEffectivePermission.Editor);
        page.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task Shared_export_returns_only_requested_readable_resources()
    {
        await using var context = Context();
        var visible = Server("owner", "visible");
        var hidden = Server("owner", "hidden");
        var label = BusinessLabel("owner", "export");
        context.AddRange(visible.Datacenter!, hidden.Datacenter!, visible, hidden, label);
        context.ServerLabels.Add(Link(visible, label));
        context.LabelGrants.Add(Grant(label, "reader", LabelGrantPermissions.Viewer));
        await context.SaveChangesAsync();

        var exported = await Repository(context).ExportServersAsync(
            "reader", CatalogView.Shared, [visible.Id, hidden.Id], Now);

        exported.Should().ContainSingle().Which.Id.Should().Be(visible.Id);
    }

    [Fact]
    public async Task Shared_detail_uses_the_same_grant_access_and_hidden_detail_fails_closed()
    {
        await using var context = Context();
        var visible = Server("owner", "visible");
        var hidden = Server("owner", "hidden");
        var label = BusinessLabel("owner", "detail");
        context.AddRange(visible.Datacenter!, hidden.Datacenter!, visible, hidden, label);
        context.ServerLabels.Add(Link(visible, label));
        context.LabelGrants.Add(Grant(label, "reader", LabelGrantPermissions.Editor));
        await context.SaveChangesAsync();

        var repository = Repository(context);
        var allowed = await repository.GetServerAsync("reader", visible.Id, Now);
        var denied = await repository.GetServerAsync("reader", hidden.Id, Now);

        allowed.Should().NotBeNull();
        allowed!.EffectivePermission.Should().Be(LabelEffectivePermission.Editor);
        allowed.Capabilities.CanEditProperties.Should().BeTrue();
        denied.Should().BeNull();
    }

    [Fact]
    public async Task Dependency_count_excludes_edges_to_applications_outside_the_readable_scope()
    {
        await using var context = Context();
        var target = new AuditNode.Domain.Entities.Application
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", AppCode = "TARGET", AppName = "Target"
        };
        var visiblePeer = new AuditNode.Domain.Entities.Application
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", AppCode = "VISIBLE", AppName = "Visible"
        };
        var hiddenPeer = new AuditNode.Domain.Entities.Application
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", AppCode = "HIDDEN", AppName = "Hidden"
        };
        var label = BusinessLabel("owner", "dependency-scope");
        context.AddRange(target, visiblePeer, hiddenPeer, label, Grant(label, "reader", LabelGrantPermissions.Viewer));
        context.ApplicationLabels.AddRange(
            new ApplicationLabel { OwnerUserId = "owner", ApplicationId = target.Id, LabelId = label.Id, Application = target, Label = label },
            new ApplicationLabel { OwnerUserId = "owner", ApplicationId = visiblePeer.Id, LabelId = label.Id, Application = visiblePeer, Label = label });
        context.AppDependencies.AddRange(
            new AppDependency { Id = Guid.NewGuid(), OwnerUserId = "owner", SourceAppId = target.Id, DestAppId = visiblePeer.Id },
            new AppDependency { Id = Guid.NewGuid(), OwnerUserId = "owner", SourceAppId = target.Id, DestAppId = hiddenPeer.Id });
        await context.SaveChangesAsync();

        var count = await Repository(context).GetDependencyCountAsync("reader", target.Id, Now);

        count.Should().Be(1);
    }

    [Fact]
    public async Task Anonymous_browse_is_single_label_viewer_only_and_rechecks_active_grant()
    {
        await using var context = Context();
        var visible = Server("owner", "visible");
        var hidden = Server("owner", "hidden");
        var label = BusinessLabel("owner", "public");
        var grant = new LabelGrant
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", LabelId = label.Id, Label = label,
            TokenHash = new string('a', 64), Permission = LabelGrantPermissions.Viewer,
            ExpiresAt = Now.AddHours(1), CreatedByUserId = "owner", CreatedAt = Now, UpdatedAt = Now
        };
        context.AddRange(visible.Datacenter!, hidden.Datacenter!, visible, hidden, label, grant);
        context.ServerLabels.Add(Link(visible, label));
        await context.SaveChangesAsync();
        var repository = Repository(context);
        var scope = new ShareTokenResolutionDto(label.Id, "owner", LabelGrantPermissions.Viewer, GrantId: grant.Id);

        var page = await repository.BrowseShareAsync(scope, "servers", Query(CatalogView.Shared), Now);

        var server = page.Items.Should().ContainSingle().Which.Server!;
        server.Id.Should().Be(visible.Id);
        server.EffectivePermission.Should().Be(LabelEffectivePermission.Viewer);
        server.Capabilities.Should().Be(CatalogCapabilities.Viewer);
        server.SharedLabelIds.Should().Equal(label.Id);

        grant.RevokedAt = Now;
        await context.SaveChangesAsync();
        (await repository.BrowseShareAsync(scope, "servers", Query(CatalogView.Shared), Now))
            .Items.Should().BeEmpty();
    }

    private static GlobalCatalogRepository Repository(AuditDbContext context) =>
        new(context, new CatalogCursorCodec(
            new DataProtectionCatalogCursorProtector(new EphemeralDataProtectionProvider())));

    private static CatalogPageQuery Query(CatalogView view, int limit = 25, string? cursor = null) =>
        new(view, limit, cursor);

    private static AuditDbContext Context()
    {
        var workspaceId = Guid.NewGuid();
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(value => value.WorkspaceId).Returns(workspaceId);
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options, tenant.Object);
    }

    private static Datacenter Datacenter(string? owner, string name) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, Name = name, Location = name
    };

    private static Server Server(string? owner, string hostname, Guid? id = null, Datacenter? datacenter = null)
    {
        datacenter ??= Datacenter(owner, $"dc-{Guid.NewGuid():N}");
        return new Server
        {
            Id = id ?? Guid.NewGuid(), OwnerUserId = owner, DatacenterId = datacenter.Id, Datacenter = datacenter,
            Hostname = hostname, IpAddress = $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}",
            OsType = "Linux", Environment = "Prod", Status = "Active"
        };
    }

    private static Label BusinessLabel(string owner, string value) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, Key = "scope", Value = value, Kind = LabelKinds.Business
    };

    private static ServerLabel Link(Server server, Label label) => new()
    {
        OwnerUserId = server.OwnerUserId, ServerId = server.Id, LabelId = label.Id, Server = server, Label = label
    };

    private static LabelGrant Grant(Label label, string grantee, string permission) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = label.OwnerUserId!, LabelId = label.Id, Label = label,
        GranteeUserId = grantee, Permission = permission, CreatedByUserId = label.OwnerUserId!, CreatedAt = Now, UpdatedAt = Now
    };
}

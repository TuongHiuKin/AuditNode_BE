using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class LabelAccessServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Owner_has_full_capabilities_without_a_grant()
    {
        await using var context = Context();
        var server = Server("owner");
        context.Servers.Add(server);
        await context.SaveChangesAsync();

        var access = await Service(context, "owner").GetServerAccessAsync(server.Id);

        access.Should().NotBeNull();
        access!.EffectivePermission.Should().Be(LabelEffectivePermission.Owner);
        access.Capabilities.Should().Be(new LabelAccessCapabilities(
            CanRead: true,
            CanEditProperties: true,
            CanCreate: true,
            CanDelete: true,
            CanChangeLabels: true,
            CanChangeOwner: false,
            CanManageGrants: true));
    }

    [Fact]
    public async Task System_admin_role_does_not_bypass_catalog_access()
    {
        await using var context = Context();
        var server = Server("another-owner");
        context.Servers.Add(server);
        await context.SaveChangesAsync();

        var access = await Service(context, "system-admin").GetServerAccessAsync(server.Id);

        access.Should().BeNull();
    }

    [Fact]
    public async Task Active_overlapping_business_grants_resolve_editor_and_limit_editor_capabilities()
    {
        await using var context = Context();
        var server = Server("owner");
        var viewerLabel = Label("owner", LabelKinds.Business);
        var editorLabel = Label("owner", LabelKinds.Business);
        context.AddRange(server, viewerLabel, editorLabel);
        context.ServerLabels.AddRange(
            Join(server, viewerLabel),
            Join(server, editorLabel));
        context.LabelGrants.AddRange(
            Grant(viewerLabel, "shared-user", LabelGrantPermissions.Viewer),
            Grant(editorLabel, "shared-user", LabelGrantPermissions.Editor));
        await context.SaveChangesAsync();

        var access = await Service(context, "shared-user").GetServerAccessAsync(server.Id);

        access.Should().NotBeNull();
        access!.EffectivePermission.Should().Be(LabelEffectivePermission.Editor);
        access.SharedLabelIds.Should().BeEquivalentTo([viewerLabel.Id, editorLabel.Id]);
        access.Capabilities.Should().Be(new LabelAccessCapabilities(
            CanRead: true,
            CanEditProperties: true,
            CanCreate: false,
            CanDelete: false,
            CanChangeLabels: false,
            CanChangeOwner: false,
            CanManageGrants: false));
    }

    [Fact]
    public async Task Owner_label_grant_covers_all_owner_resources_without_join_rows()
    {
        await using var context = Context();
        var server = Server("owner");
        var application = App("owner");
        var ownerLabel = Label("owner", LabelKinds.Owner);
        context.AddRange(server, application, ownerLabel, Grant(ownerLabel, "viewer", LabelGrantPermissions.Viewer));
        await context.SaveChangesAsync();

        var service = Service(context, "viewer");
        (await service.GetReadableServerIdsAsync(CatalogView.Shared)).Should().Equal(server.Id);
        (await service.GetReadableApplicationIdsAsync(CatalogView.Shared)).Should().Equal(application.Id);
    }

    [Fact]
    public async Task Business_label_grant_resolves_joined_application()
    {
        await using var context = Context();
        var application = App("owner");
        var label = Label("owner", LabelKinds.Business);
        context.AddRange(application, label);
        context.ApplicationLabels.Add(new ApplicationLabel
        {
            OwnerUserId = application.OwnerUserId,
            ApplicationId = application.Id,
            LabelId = label.Id
        });
        context.LabelGrants.Add(Grant(label, "editor", LabelGrantPermissions.Editor));
        await context.SaveChangesAsync();

        var access = await Service(context, "editor").GetApplicationAccessAsync(application.Id);

        access.Should().NotBeNull();
        access!.EffectivePermission.Should().Be(LabelEffectivePermission.Editor);
        access.Capabilities.CanEditProperties.Should().BeTrue();
        access.Capabilities.CanChangeLabels.Should().BeFalse();
    }

    [Fact]
    public async Task Revoked_and_expired_grants_fail_closed_and_mine_shared_do_not_mix()
    {
        await using var context = Context();
        var mine = Server("viewer");
        var shared = Server("owner");
        var expired = Server("expired-owner");
        var revoked = Server("revoked-owner");
        var activeLabel = Label("owner", LabelKinds.Business);
        var expiredLabel = Label("expired-owner", LabelKinds.Business);
        var revokedLabel = Label("revoked-owner", LabelKinds.Business);
        context.AddRange(mine, shared, expired, revoked, activeLabel, expiredLabel, revokedLabel);
        context.ServerLabels.AddRange(
            Join(shared, activeLabel),
            Join(expired, expiredLabel),
            Join(revoked, revokedLabel));
        var revokedGrant = Grant(revokedLabel, "viewer", LabelGrantPermissions.Editor);
        revokedGrant.RevokedAt = Now.UtcDateTime;
        context.LabelGrants.AddRange(
            Grant(activeLabel, "viewer", LabelGrantPermissions.Viewer),
            Grant(expiredLabel, "viewer", LabelGrantPermissions.Editor, expiresAt: Now.UtcDateTime),
            revokedGrant);
        await context.SaveChangesAsync();

        var service = Service(context, "viewer");
        (await service.GetReadableServerIdsAsync(CatalogView.Mine)).Should().Equal(mine.Id);
        (await service.GetReadableServerIdsAsync(CatalogView.Shared)).Should().Equal(shared.Id);
        (await service.GetServerAccessAsync(expired.Id)).Should().BeNull();
        (await service.GetServerAccessAsync(revoked.Id)).Should().BeNull();
    }

    private static LabelAccessService Service(AuditDbContext context, string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(userId);
        return new LabelAccessService(context, currentUser.Object, new FixedTimeProvider(Now));
    }

    private static AuditDbContext Context()
    {
        return new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
    }

    private static Server Server(string? owner) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = owner,
        DatacenterId = Guid.NewGuid(),
        Hostname = Guid.NewGuid().ToString("N"),
        IpAddress = Guid.NewGuid().ToString("N"),
        OsType = "Linux",
        Environment = "Test",
        Status = "Up"
    };

    private static AuditNode.Domain.Entities.Application App(string owner) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner,
        AppCode = Guid.NewGuid().ToString("N"), AppName = "App", OwnerTeam = "Team", Risk = "Low"
    };

    private static Label Label(string owner, string kind) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner,
        Key = kind, Value = Guid.NewGuid().ToString("N"), Kind = kind,
        IsProtected = kind == LabelKinds.Owner
    };

    private static ServerLabel Join(Server server, Label label) => new()
    {
        OwnerUserId = server.OwnerUserId,
        ServerId = server.Id, LabelId = label.Id
    };

    private static LabelGrant Grant(
        Label label,
        string userId,
        string permission,
        DateTime? expiresAt = null) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = label.OwnerUserId!, LabelId = label.Id,
        GranteeUserId = userId, Permission = permission, ExpiresAt = expiresAt,
        Version = 1, CreatedByUserId = label.OwnerUserId!
    };
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

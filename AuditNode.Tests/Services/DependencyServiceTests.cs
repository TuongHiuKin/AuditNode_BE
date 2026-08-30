using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Services;

public class DependencyServiceTests
{
    [Fact]
    public async Task Sync_uses_owner_catalog_revision_instead_of_workspace_revision()
    {
        await using var context = Context();
        var fixture = await SeedValidDependencyReferences(context);
        context.OwnerCatalogStates.Add(new OwnerCatalogState
        {
            OwnerUserId = "owner",
            TopologyVersion = 0
        });
        await context.SaveChangesAsync();

        var status = await Service(context).SyncDependenciesAsync(
            Dto(fixture.SourceAppId, fixture.DestinationAppId, fixture.DestinationPortId));

        status.Should().Be(DependencySyncStatus.Success);
        (await context.OwnerCatalogStates.SingleAsync()).TopologyVersion.Should().Be(1);
        (await context.Workspaces.SingleAsync()).TopologyVersion.Should().Be(0);
    }

    [Fact]
    public async Task Sync_rejects_cross_owner_endpoints_even_inside_the_same_transitional_workspace()
    {
        await using var context = Context();
        var fixture = await SeedValidDependencyReferences(context);
        var destination = await context.Applications.SingleAsync(item => item.Id == fixture.DestinationAppId);
        destination.OwnerUserId = "other-owner";
        var mapping = await context.PortMappings.SingleAsync(item => item.Id == fixture.DestinationPortId);
        mapping.OwnerUserId = "other-owner";
        await context.SaveChangesAsync();

        var status = await Service(context).SyncDependenciesAsync(
            Dto(fixture.SourceAppId, fixture.DestinationAppId, fixture.DestinationPortId));

        status.Should().Be(DependencySyncStatus.Forbidden);
        context.AppDependencies.Should().BeEmpty();
    }

    [Fact]
    public async Task Owner_can_clear_unreferenced_dependencies_with_an_empty_sync()
    {
        await using var context = Context();
        var fixture = await SeedValidDependencyReferences(context);
        context.AppDependencies.Add(new AppDependency
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", SourceAppId = fixture.SourceAppId,
            DestAppId = fixture.DestinationAppId, DestPortId = fixture.DestinationPortId,
            ConnectionType = "Automatic"
        });
        await context.SaveChangesAsync();

        var status = await Service(context).SyncDependenciesAsync(new SyncDependenciesDto
        {
            Version = 0,
            Dependencies = []
        });

        status.Should().Be(DependencySyncStatus.Success);
        context.AppDependencies.Should().BeEmpty();
        (await context.OwnerCatalogStates.SingleAsync()).TopologyVersion.Should().Be(1);
    }

    [Fact]
    public async Task Repeated_sync_is_idempotent_and_preserves_dependency_identity()
    {
        await using var context = Context();
        var fixture = await SeedValidDependencyReferences(context);
        var dto = Dto(fixture.SourceAppId, fixture.DestinationAppId, fixture.DestinationPortId);
        var service = Service(context);

        var first = await service.SyncDependenciesAsync(dto);
        var firstEntity = await context.AppDependencies.SingleAsync();
        dto.Version = 1;
        var second = await service.SyncDependenciesAsync(dto);

        first.Should().Be(DependencySyncStatus.Success);
        second.Should().Be(DependencySyncStatus.Success);
        context.AppDependencies.Should().ContainSingle().Which.Id.Should().Be(firstEntity.Id);
    }

    [Fact]
    public async Task Sync_rejects_self_loop_without_mutation()
    {
        await using var context = Context();
        var fixture = await SeedValidDependencyReferences(context);

        var status = await Service(context).SyncDependenciesAsync(
            Dto(fixture.DestinationAppId, fixture.DestinationAppId, fixture.DestinationPortId));

        status.Should().Be(DependencySyncStatus.SelfLoop);
        context.AppDependencies.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_rejects_duplicate_payload_edges()
    {
        await using var context = Context();
        var fixture = await SeedValidDependencyReferences(context);
        var item = new DependencyItemDto
        {
            SourceAppId = fixture.SourceAppId,
            DestAppId = fixture.DestinationAppId,
            DestinationPortMappingId = fixture.DestinationPortId
        };

        var status = await Service(context).SyncDependenciesAsync(new SyncDependenciesDto
        {
            Version = 0,
            Dependencies = [item, new DependencyItemDto
            {
                SourceAppId = item.SourceAppId, DestAppId = item.DestAppId,
                DestinationPortMappingId = item.DestinationPortMappingId
            }]
        });

        status.Should().Be(DependencySyncStatus.Duplicate);
    }

    [Fact]
    public async Task Destination_mapping_must_belong_to_destination_application()
    {
        await using var context = Context();
        var fixture = await SeedValidDependencyReferences(context);
        var otherDestination = new AppEntity { Id = Guid.NewGuid(), OwnerUserId = "owner", AppCode = "OTHER", AppName = "Other" };
        context.Applications.Add(otherDestination);
        await context.SaveChangesAsync();

        var status = await Service(context).SyncDependenciesAsync(
            Dto(fixture.SourceAppId, otherDestination.Id, fixture.DestinationPortId));

        status.Should().Be(DependencySyncStatus.DestinationMismatch);
    }

    [Fact]
    public async Task Cross_workspace_or_unknown_references_are_not_found()
    {
        await using var context = Context();
        var fixture = await SeedValidDependencyReferences(context);

        var status = await Service(context).SyncDependenciesAsync(
            Dto(Guid.NewGuid(), fixture.DestinationAppId, fixture.DestinationPortId));

        status.Should().Be(DependencySyncStatus.NotFound);
    }

    [Fact]
    public async Task Editor_cannot_use_full_replacement_sync_or_delete_dependencies_outside_the_grant()
    {
        await using var context = Context();
        var visible = await SeedValidDependencyReferences(context);
        var hiddenSource = new AppEntity { Id = Guid.NewGuid(), OwnerUserId = "owner", AppCode = "HIDDEN-S", AppName = "Hidden source" };
        var hiddenDestination = new AppEntity { Id = Guid.NewGuid(), OwnerUserId = "owner", AppCode = "HIDDEN-D", AppName = "Hidden destination" };
        var hiddenPort = new PortMapping
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", AppId = hiddenDestination.Id,
            ServerId = Guid.NewGuid(), PortNumber = 8443
        };
        var hiddenDependency = new AppDependency
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", SourceAppId = hiddenSource.Id,
            DestAppId = hiddenDestination.Id, DestPortId = hiddenPort.Id, ConnectionType = "Manual"
        };
        var label = new Label
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", Key = "scope", Value = "visible", Kind = LabelKinds.Business
        };
        context.AddRange(hiddenSource, hiddenDestination, hiddenPort, hiddenDependency, label,
            new ApplicationLabel { OwnerUserId = "owner", ApplicationId = visible.SourceAppId, LabelId = label.Id },
            new ApplicationLabel { OwnerUserId = "owner", ApplicationId = visible.DestinationAppId, LabelId = label.Id },
            new LabelGrant
            {
                Id = Guid.NewGuid(), OwnerUserId = "owner", LabelId = label.Id, GranteeUserId = "editor",
                Permission = LabelGrantPermissions.Editor, CreatedByUserId = "owner"
            });
        await context.SaveChangesAsync();

        var status = await Service(context, "editor").SyncDependenciesAsync(
            Dto(visible.SourceAppId, visible.DestinationAppId, visible.DestinationPortId));

        status.Should().Be(DependencySyncStatus.Forbidden);
        (await context.AppDependencies.SingleAsync()).Id.Should().Be(hiddenDependency.Id);
    }

    [Fact]
    public async Task Destination_mapping_in_another_transitional_workspace_is_rejected_before_mutation()
    {
        var databaseName = Guid.NewGuid().ToString();
        var primaryWorkspaceId = Guid.NewGuid();
        await using var context = Context(primaryWorkspaceId, databaseName);
        var fixture = await SeedValidDependencyReferences(context);
        var otherWorkspaceId = Guid.NewGuid();
        var crossWorkspacePort = new PortMapping
        {
            Id = Guid.NewGuid(), WorkspaceId = otherWorkspaceId, OwnerUserId = "owner",
            AppId = fixture.DestinationAppId, ServerId = Guid.NewGuid(), PortNumber = 9443
        };
        await using (var otherContext = Context(otherWorkspaceId, databaseName))
        {
            otherContext.PortMappings.Add(crossWorkspacePort);
            await otherContext.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();

        var status = await Service(context).SyncDependenciesAsync(
            Dto(fixture.SourceAppId, fixture.DestinationAppId, crossWorkspacePort.Id));

        status.Should().Be(DependencySyncStatus.Forbidden);
        context.AppDependencies.Should().BeEmpty();
    }

    private static AuditDbContext Context(Guid? workspaceId = null, string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var tenant = new Mock<ITenantProvider>();
        var selectedWorkspaceId = workspaceId ?? Guid.NewGuid();
        tenant.SetupGet(x => x.WorkspaceId).Returns(selectedWorkspaceId);
        var context = new AuditDbContext(options, tenant.Object);
        if (!context.Workspaces.IgnoreQueryFilters().Any(item => item.Id == selectedWorkspaceId))
        {
            context.Workspaces.Add(new Workspace { Id = selectedWorkspaceId, Name = "Dependency test", OwnerUserId = "owner" });
            context.SaveChanges();
        }
        return context;
    }

    private static DependencyService Service(AuditDbContext context, string actorUserId = "owner")
    {
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(item => item.WorkspaceId).Returns(context.CurrentWorkspaceId);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(item => item.UserId).Returns(actorUserId);
        return new DependencyService(
            context,
            NullLogger<DependencyService>.Instance,
            user.Object);
    }

    private static SyncDependenciesDto Dto(Guid source, Guid destination, Guid destinationPort) => new()
    {
        Version = 0,
        Dependencies = [new DependencyItemDto
        {
            SourceAppId = source, DestAppId = destination, DestinationPortMappingId = destinationPort
        }]
    };

    private static async Task<(Guid SourceAppId, Guid DestinationAppId, Guid DestinationPortId)>
        SeedValidDependencyReferences(AuditDbContext context)
    {
        var source = new AppEntity { Id = Guid.NewGuid(), OwnerUserId = "owner", AppCode = "SOURCE", AppName = "Source" };
        var destination = new AppEntity { Id = Guid.NewGuid(), OwnerUserId = "owner", AppCode = "DEST", AppName = "Destination" };
        var mapping = new PortMapping
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", AppId = destination.Id, ServerId = Guid.NewGuid(), PortNumber = 443
        };
        context.AddRange(source, destination, mapping);
        await context.SaveChangesAsync();
        return (source.Id, destination.Id, mapping.Id);
    }
}

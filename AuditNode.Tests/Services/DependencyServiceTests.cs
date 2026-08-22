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
    public async Task Repeated_sync_is_idempotent_and_preserves_dependency_identity()
    {
        await using var context = Context();
        var fixture = await SeedValidDependencyReferences(context);
        var dto = Dto(fixture.SourceAppId, fixture.DestinationAppId, fixture.DestinationPortId);
        var service = Service(context);

        var first = await service.SyncDependenciesAsync(dto);
        var firstEntity = await context.AppDependencies.SingleAsync();
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
        var otherDestination = new AppEntity { Id = Guid.NewGuid(), AppCode = "OTHER", AppName = "Other" };
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

    private static AuditDbContext Context()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(x => x.WorkspaceId).Returns(Guid.NewGuid());
        return new AuditDbContext(options, tenant.Object);
    }

    private static DependencyService Service(AuditDbContext context) =>
        new(context, NullLogger<DependencyService>.Instance);

    private static SyncDependenciesDto Dto(Guid source, Guid destination, Guid destinationPort) => new()
    {
        Dependencies = [new DependencyItemDto
        {
            SourceAppId = source, DestAppId = destination, DestinationPortMappingId = destinationPort
        }]
    };

    private static async Task<(Guid SourceAppId, Guid DestinationAppId, Guid DestinationPortId)>
        SeedValidDependencyReferences(AuditDbContext context)
    {
        var source = new AppEntity { Id = Guid.NewGuid(), AppCode = "SOURCE", AppName = "Source" };
        var destination = new AppEntity { Id = Guid.NewGuid(), AppCode = "DEST", AppName = "Destination" };
        var mapping = new PortMapping
        {
            Id = Guid.NewGuid(), AppId = destination.Id, ServerId = Guid.NewGuid(), PortNumber = 443
        };
        context.AddRange(source, destination, mapping);
        await context.SaveChangesAsync();
        return (source.Id, destination.Id, mapping.Id);
    }
}

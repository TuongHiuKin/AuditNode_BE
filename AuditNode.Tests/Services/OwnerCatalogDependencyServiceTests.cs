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

public sealed class OwnerCatalogDependencyServiceTests
{
    [Fact]
    public async Task Full_replacement_rejects_cross_owner_endpoints_without_mutation()
    {
        await using var context = CreateContext();
        var source = App("owner-a", "SOURCE");
        var destination = App("owner-b", "DEST");
        var mapping = Mapping(destination, "owner-b");
        context.AddRange(source, destination, mapping);
        await context.SaveChangesAsync();

        var result = await Service(context, "owner-a").SyncDependenciesAsync(Request(0, source.Id, destination.Id, mapping.Id));

        result.Should().Be(DependencySyncStatus.Forbidden);
        (await context.AppDependencies.CountAsync()).Should().Be(0);
        (await context.OwnerCatalogStates.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Scoped_editor_cannot_use_owner_full_replacement_or_delete_existing_edges()
    {
        await using var context = CreateContext();
        var source = App("owner", "SOURCE");
        var destination = App("owner", "DEST");
        var mapping = Mapping(destination, "owner");
        var dependency = new AppDependency
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", SourceAppId = source.Id,
            DestAppId = destination.Id, DestPortId = mapping.Id, ConnectionType = "HTTPS"
        };
        context.AddRange(source, destination, mapping, dependency);
        await context.SaveChangesAsync();

        var result = await Service(context, "editor").SyncDependenciesAsync(
            Request(0, source.Id, destination.Id, mapping.Id));

        result.Should().Be(DependencySyncStatus.Forbidden);
        (await context.AppDependencies.FindAsync(dependency.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Owner_full_replacement_is_versioned_and_can_remove_unreferenced_dependencies()
    {
        await using var context = CreateContext();
        var source = App("owner", "SOURCE");
        var destination = App("owner", "DEST");
        var mapping = Mapping(destination, "owner");
        context.AddRange(source, destination, mapping);
        await context.SaveChangesAsync();
        var service = Service(context, "owner");

        (await service.SyncDependenciesAsync(Request(0, source.Id, destination.Id, mapping.Id)))
            .Should().Be(DependencySyncStatus.Success);
        (await context.AppDependencies.CountAsync()).Should().Be(1);
        (await context.OwnerCatalogStates.FindAsync("owner"))!.TopologyVersion.Should().Be(1);

        (await service.SyncDependenciesAsync(new SyncDependenciesDto { Version = 1, Dependencies = [] }))
            .Should().Be(DependencySyncStatus.Success);
        (await context.AppDependencies.CountAsync()).Should().Be(0);
        (await context.OwnerCatalogStates.FindAsync("owner"))!.TopologyVersion.Should().Be(2);
    }

    private static DependencyService Service(AuditDbContext context, string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(item => item.UserId).Returns(userId);
        return new DependencyService(context, NullLogger<DependencyService>.Instance, currentUser.Object);
    }

    private static SyncDependenciesDto Request(long version, Guid sourceId, Guid destinationId, Guid mappingId) => new()
    {
        Version = version,
        Dependencies = [new DependencyItemDto
        {
            SourceAppId = sourceId,
            DestAppId = destinationId,
            DestinationPortMappingId = mappingId
        }]
    };

    private static AppEntity App(string owner, string code) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, AppCode = code, AppName = code, OwnerTeam = "Team"
    };

    private static PortMapping Mapping(AppEntity application, string owner) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, AppId = application.Id,
        ServerId = Guid.NewGuid(), PortNumber = 443, Protocol = "HTTPS"
    };

    private static AuditDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options);
    }
}

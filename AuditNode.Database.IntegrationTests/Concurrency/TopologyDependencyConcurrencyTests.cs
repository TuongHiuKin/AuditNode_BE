using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Database.IntegrationTests.Concurrency;

[Trait("Category", "PostgreSQL")]
[Trait("Suite", "Relational")]
[Collection(PostgreSqlCollection.Name)]
public sealed class TopologyDependencyConcurrencyTests(PostgreSqlDatabaseFixture database)
{
    [Fact]
    public async Task Full_save_and_dependency_sync_allow_one_coherent_revision_winner()
    {
        var fixture = await SeedAsync(database.ConnectionString);
        await using var fullSaveContext = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        await using var dependencyContext = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        var fullSave = Repository(fullSaveContext, fixture.Owner).SaveTopologyStateAsync(new SaveTopologyStateDto
        {
            Version = 0,
            Nodes = [],
            Edges = [],
            Dependencies = []
        });
        var dependencySync = DependencyService(dependencyContext, fixture.Owner).SyncDependenciesAsync(
            new SyncDependenciesDto
            {
                Version = 0,
                Dependencies =
                [
                    new DependencyItemDto
                    {
                        SourceAppId = fixture.SourceAppId,
                        DestAppId = fixture.DestinationAppId,
                        DestinationPortMappingId = fixture.DestinationPortId
                    }
                ]
            });

        await Task.WhenAll(fullSave, dependencySync);

        var fullSaveStatus = await fullSave;
        var dependencyStatus = await dependencySync;
        var fullSaveWon = fullSaveStatus == TopologyStateStatus.Success;
        var dependencyWon = dependencyStatus == DependencySyncStatus.Success;
        (fullSaveWon ? 1 : 0).Should().Be(1 - (dependencyWon ? 1 : 0));
        fullSaveStatus.Should().BeOneOf(TopologyStateStatus.Success, TopologyStateStatus.Conflict);
        dependencyStatus.Should().BeOneOf(DependencySyncStatus.Success, DependencySyncStatus.Conflict);

        await using var verification = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        var state = await verification.OwnerCatalogStates.SingleAsync(item => item.OwnerUserId == fixture.Owner);
        state.TopologyVersion.Should().Be(1);
        var dependencies = await verification.AppDependencies
            .Where(item => item.OwnerUserId == fixture.Owner)
            .ToListAsync();
        if (fullSaveWon)
            dependencies.Should().BeEmpty();
        else
            dependencies.Should().ContainSingle(item => item.Id == fixture.DependencyId);
    }

    private static TopologyRepository Repository(
        AuditNode.Infrastructure.Data.AuditDbContext context,
        string owner)
    {
        var user = User(owner);
        return new TopologyRepository(context, user, new OwnerGraphAccessService(context, user, TimeProvider.System));
    }

    private static DependencyService DependencyService(
        AuditNode.Infrastructure.Data.AuditDbContext context,
        string owner) => new(context, NullLogger<DependencyService>.Instance, User(owner));

    private static ICurrentUserService User(string owner)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(service => service.UserId).Returns(owner);
        return user.Object;
    }

    private static async Task<Fixture> SeedAsync(string connectionString)
    {
        var owner = $"topology-dependency-owner-{Guid.NewGuid():N}";
        var datacenter = new Datacenter
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, Name = "Topology dependency", Location = "Test"
        };
        var server = new Server
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, DatacenterId = datacenter.Id,
            Hostname = "topology-dependency", IpAddress = "10.242.1.1", OsType = "Linux",
            Environment = "Test", Status = "Up"
        };
        var source = App(owner, "TOPOLOGY-SOURCE");
        var destination = App(owner, "TOPOLOGY-DESTINATION");
        var port = new PortMapping
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, ServerId = server.Id, AppId = destination.Id,
            PortNumber = 65201, Protocol = "TCP"
        };
        var dependency = new AppDependency
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, SourceAppId = source.Id,
            DestAppId = destination.Id, DestPortId = port.Id,
            ConnectionType = "Automatic", CreatedAt = DateTime.UtcNow
        };
        var state = new OwnerCatalogState { OwnerUserId = owner, TopologyVersion = 0 };

        await using var context = PostgreSqlTestEnvironment.CreateContext(connectionString);
        context.AddRange(datacenter, server, source, destination, port, dependency, state);
        await context.SaveChangesAsync();
        return new Fixture(owner, source.Id, destination.Id, port.Id, dependency.Id);
    }

    private static AppEntity App(string owner, string code) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, AppCode = code, AppName = code,
        OwnerTeam = "Test", Risk = "Low", Icon = string.Empty, TechStack = string.Empty
    };

    private sealed record Fixture(
        string Owner,
        Guid SourceAppId,
        Guid DestinationAppId,
        Guid DestinationPortId,
        Guid DependencyId);
}

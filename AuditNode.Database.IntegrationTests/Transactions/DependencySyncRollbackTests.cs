using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Database.IntegrationTests.Transactions;

[Trait("Category", "PostgreSQL")]
[Trait("Suite", "Relational")]
[Collection(PostgreSqlCollection.Name)]
public sealed class DependencySyncRollbackTests(PostgreSqlDatabaseFixture database)
{
    [Fact]
    public async Task Insert_failure_restores_deleted_dependency_and_preserves_revision()
    {
        var fixture = await SeedAsync(database.ConnectionString);
        await using var failure = await PostgreSqlFailureTrigger.ForDependencyAsync(
            database.ConnectionString, fixture.NewPortId);
        await using var context = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        var action = () => Service(context, fixture.Owner).SyncDependenciesAsync(new SyncDependenciesDto
        {
            Version = fixture.Version,
            Dependencies =
            [
                new DependencyItemDto
                {
                    SourceAppId = fixture.SourceAppId,
                    DestAppId = fixture.NewDestinationAppId,
                    DestinationPortMappingId = fixture.NewPortId
                }
            ]
        });

        await action.Should().ThrowAsync<DbUpdateException>();

        await using var verification = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        var dependencies = await verification.AppDependencies
            .Where(item => item.OwnerUserId == fixture.Owner)
            .ToListAsync();
        dependencies.Should().ContainSingle(item => item.Id == fixture.OriginalDependencyId);
        dependencies.Should().NotContain(item => item.DestPortId == fixture.NewPortId);
        var state = await verification.OwnerCatalogStates.SingleAsync(item => item.OwnerUserId == fixture.Owner);
        state.TopologyVersion.Should().Be(fixture.Version);
        state.UpdatedAt.Should().Be(fixture.OriginalUpdatedAt);
    }

    private static DependencyService Service(
        AuditNode.Infrastructure.Data.AuditDbContext context,
        string owner)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(owner);
        return new DependencyService(context, NullLogger<DependencyService>.Instance, currentUser.Object);
    }

    private static async Task<Fixture> SeedAsync(string connectionString)
    {
        var owner = $"dependency-owner-{Guid.NewGuid():N}";
        var datacenter = new Datacenter
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, Name = "Dependency rollback", Location = "Test"
        };
        var server = new Server
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, DatacenterId = datacenter.Id,
            Hostname = "dependency-server", IpAddress = "10.241.1.1", OsType = "Linux",
            Environment = "Test", Status = "Up"
        };
        var source = App(owner, "ROLLBACK-SOURCE");
        var oldDestination = App(owner, "ROLLBACK-OLD");
        var newDestination = App(owner, "ROLLBACK-NEW");
        var oldPort = Port(owner, server.Id, oldDestination.Id, 65101);
        var newPort = Port(owner, server.Id, newDestination.Id, 65102);
        var dependency = new AppDependency
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, SourceAppId = source.Id,
            DestAppId = oldDestination.Id, DestPortId = oldPort.Id,
            ConnectionType = "Automatic", CreatedAt = DateTime.UtcNow
        };
        const long version = 7;
        var updatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var state = new OwnerCatalogState
        {
            OwnerUserId = owner, TopologyVersion = version, UpdatedAt = updatedAt
        };

        await using var context = PostgreSqlTestEnvironment.CreateContext(connectionString);
        context.AddRange(datacenter, server, source, oldDestination, newDestination, oldPort, newPort, dependency, state);
        await context.SaveChangesAsync();
        return new Fixture(owner, source.Id, newDestination.Id, newPort.Id, dependency.Id, version, updatedAt);
    }

    private static AppEntity App(string owner, string code) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, AppCode = code, AppName = code,
        OwnerTeam = "Test", Risk = "Low", Icon = string.Empty, TechStack = string.Empty
    };

    private static PortMapping Port(string owner, Guid serverId, Guid appId, int port) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, ServerId = serverId, AppId = appId,
        PortNumber = port, Protocol = "TCP"
    };

    private sealed record Fixture(
        string Owner,
        Guid SourceAppId,
        Guid NewDestinationAppId,
        Guid NewPortId,
        Guid OriginalDependencyId,
        long Version,
        DateTime OriginalUpdatedAt);
}

using AuditNode.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditNode.Database.IntegrationTests.Constraints;

[Trait("Category", "PostgreSQL")]
[Trait("Suite", "Relational")]
[Collection(PostgreSqlCollection.Name)]
public sealed class OwnerScopedConstraintTests(PostgreSqlDatabaseFixture database)
{
    [Fact]
    public async Task Server_cannot_reference_another_owners_datacenter()
    {
        var owner = $"constraint-owner-{Guid.NewGuid():N}";
        var otherOwner = $"constraint-other-{Guid.NewGuid():N}";
        var datacenter = Datacenter(owner, "Owner datacenter");
        var rejectedServer = Server(otherOwner, datacenter.Id, "10.243.1.1", "cross-owner-server");

        await using (var seed = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString))
        {
            seed.Datacenters.Add(datacenter);
            await seed.SaveChangesAsync();
        }

        await using (var mutation = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString))
        {
            mutation.Servers.Add(rejectedServer);
            var action = () => mutation.SaveChangesAsync();

            var exception = await action.Should().ThrowAsync<DbUpdateException>();
            exception.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        }

        await using var verification = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        (await verification.Servers.AnyAsync(item => item.Id == rejectedServer.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Server_ip_is_unique_per_owner_but_can_be_reused_by_a_different_owner()
    {
        var firstOwner = $"constraint-first-{Guid.NewGuid():N}";
        var secondOwner = $"constraint-second-{Guid.NewGuid():N}";
        var firstDatacenter = Datacenter(firstOwner, "First owner datacenter");
        var secondDatacenter = Datacenter(secondOwner, "Second owner datacenter");
        const string sharedIp = "10.243.2.1";
        var original = Server(firstOwner, firstDatacenter.Id, sharedIp, "original-server");

        await using (var seed = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString))
        {
            seed.AddRange(firstDatacenter, secondDatacenter, original);
            await seed.SaveChangesAsync();
        }

        var rejectedDuplicate = Server(firstOwner, firstDatacenter.Id, sharedIp, "duplicate-server");
        await using (var duplicate = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString))
        {
            duplicate.Servers.Add(rejectedDuplicate);
            var action = () => duplicate.SaveChangesAsync();

            var exception = await action.Should().ThrowAsync<DbUpdateException>();
            exception.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }

        var allowedForSecondOwner = Server(secondOwner, secondDatacenter.Id, sharedIp, "second-owner-server");
        await using (var allowed = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString))
        {
            allowed.Servers.Add(allowedForSecondOwner);
            await allowed.SaveChangesAsync();
        }

        await using var verification = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        (await verification.Servers.CountAsync(item =>
            item.OwnerUserId == firstOwner && item.IpAddress == sharedIp)).Should().Be(1);
        (await verification.Servers.CountAsync(item =>
            item.OwnerUserId == secondOwner && item.IpAddress == sharedIp)).Should().Be(1);
        (await verification.Servers.AnyAsync(item => item.Id == rejectedDuplicate.Id)).Should().BeFalse();
        (await verification.Servers.AnyAsync(item => item.Id == allowedForSecondOwner.Id)).Should().BeTrue();
    }

    private static Datacenter Datacenter(string owner, string name) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = owner,
        Name = name,
        Location = "Test"
    };

    private static Server Server(string owner, Guid datacenterId, string ipAddress, string hostname) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = owner,
        DatacenterId = datacenterId,
        IpAddress = ipAddress,
        Hostname = hostname,
        OsType = "Linux",
        Environment = "Test",
        Status = "Up"
    };
}

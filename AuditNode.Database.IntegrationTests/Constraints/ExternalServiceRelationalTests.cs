using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Database.IntegrationTests.Constraints;

/// <summary>
/// Covers the parts of the external-service contract that only PostgreSQL can prove:
/// case-insensitive search (ILIKE) and the database-level uniqueness and
/// single-destination constraints.
/// </summary>
[Trait("Category", "PostgreSQL")]
[Trait("Suite", "Relational")]
[Collection(PostgreSqlCollection.Name)]
public sealed class ExternalServiceRelationalTests(PostgreSqlDatabaseFixture database)
{
    private const string Owner = "external-owner-1";
    private const string OtherOwner = "external-owner-2";

    [Fact]
    public async Task Search_is_case_insensitive_across_name_kind_and_provider()
    {
        await using var context = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        await CleanupAsync(context);
        var service = Service(context, Owner);
        await service.CreateAsync(new CreateExternalServiceDto
        {
            Name = "Stripe payments", Kind = "SaaS", Environment = "Production", Provider = "Stripe, Inc.",
        });
        await service.CreateAsync(new CreateExternalServiceDto
        {
            Name = "Legacy relay", Kind = "SMTP", Environment = "Production", Provider = "Internal",
        });

        (await service.ListAsync("STRIPE")).Should().ContainSingle().Which.Name.Should().Be("Stripe payments");
        (await service.ListAsync("smtp")).Should().ContainSingle().Which.Name.Should().Be("Legacy relay");
        (await service.ListAsync("internal")).Should().ContainSingle().Which.Name.Should().Be("Legacy relay");
        (await service.ListAsync("nothing-matches")).Should().BeEmpty();
    }

    [Fact]
    public async Task The_same_name_is_allowed_for_different_owners_but_not_within_one()
    {
        await using var context = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        await CleanupAsync(context);
        var request = new CreateExternalServiceDto { Name = "Stripe", Kind = "SaaS", Environment = "Production" };

        var first = await Service(context, Owner).CreateAsync(request);
        var duplicate = await Service(context, Owner).CreateAsync(request);
        var otherOwner = await Service(context, OtherOwner).CreateAsync(request);

        first.Should().NotBeNull();
        duplicate.Should().BeNull();
        otherOwner.Should().NotBeNull();
    }

    [Fact]
    public async Task A_dependency_must_target_exactly_one_destination()
    {
        await using var context = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        await CleanupAsync(context);
        var external = await Service(context, Owner).CreateAsync(
            new CreateExternalServiceDto { Name = "Stripe", Kind = "SaaS", Environment = "Production" });
        var sourceAppId = await CreateApplicationAsync(context, Owner, "APP-FOR-1");

        // Both destinations set: refused by ck_app_dependencies_single_destination.
        context.AppDependencies.Add(new Domain.Entities.AppDependency
        {
            Id = Guid.NewGuid(),
            OwnerUserId = Owner,
            SourceAppId = sourceAppId,
            DestAppId = Guid.NewGuid(),
            DestPortId = Guid.NewGuid(),
            ExternalServiceId = external!.Id,
            ConnectionType = "HTTPS",
        });

        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task A_dependency_with_no_destination_is_refused()
    {
        await using var context = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        await CleanupAsync(context);
        var sourceAppId = await CreateApplicationAsync(context, Owner, "APP-FOR-2");

        context.AppDependencies.Add(new Domain.Entities.AppDependency
        {
            Id = Guid.NewGuid(),
            OwnerUserId = Owner,
            SourceAppId = sourceAppId,
            ConnectionType = "HTTPS",
        });

        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Deleting_an_external_service_cascades_its_dependencies()
    {
        await using var context = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        await CleanupAsync(context);
        var service = Service(context, Owner);
        var external = await service.CreateAsync(
            new CreateExternalServiceDto { Name = "Stripe", Kind = "SaaS", Environment = "Production" });
        context.AppDependencies.Add(new Domain.Entities.AppDependency
        {
            Id = Guid.NewGuid(),
            OwnerUserId = Owner,
            SourceAppId = await CreateApplicationAsync(context, Owner, "APP-FOR-3"),
            ExternalServiceId = external!.Id,
            ConnectionType = "HTTPS",
        });
        await context.SaveChangesAsync();

        var deleted = await service.DeleteAsync(external.Id);

        deleted.Should().BeTrue();
        (await context.ExternalServices.CountAsync(item => item.OwnerUserId == Owner)).Should().Be(0);
        (await context.AppDependencies.CountAsync(item => item.OwnerUserId == Owner)).Should().Be(0);
    }

    /// <summary>
    /// Creates a real application row so the owner-scoped foreign key on
    /// app_dependencies.source_app_id is satisfied.
    /// </summary>
    private static async Task<Guid> CreateApplicationAsync(
        Infrastructure.Data.AuditDbContext context, string ownerUserId, string code)
    {
        var app = new Domain.Entities.Application
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            AppCode = code,
            AppName = code,
            OwnerTeam = "test",
            Risk = "Low",
            Icon = "app",
            TechStack = "test",
        };
        context.Applications.Add(app);
        await context.SaveChangesAsync();
        return app.Id;
    }

    /// <summary>
    /// The suite shares one database, so each test removes only its own owner rows.
    /// </summary>
    private static async Task CleanupAsync(Infrastructure.Data.AuditDbContext context)
    {
        await context.AppDependencies
            .Where(item => item.OwnerUserId == Owner || item.OwnerUserId == OtherOwner)
            .ExecuteDeleteAsync();
        await context.ExternalServices
            .Where(item => item.OwnerUserId == Owner || item.OwnerUserId == OtherOwner)
            .ExecuteDeleteAsync();
        await context.Applications
            .Where(item => item.OwnerUserId == Owner || item.OwnerUserId == OtherOwner)
            .ExecuteDeleteAsync();
    }

    private static ExternalServiceService Service(
        Infrastructure.Data.AuditDbContext context, string ownerUserId) =>
        new(context, new StubCurrentUser(ownerUserId), TimeProvider.System);

    private sealed class StubCurrentUser(string? userId) : ICurrentUserService
    {
        public string? UserId { get; } = userId;
    }
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuditNode.Tests.Services;

/// <summary>
/// Covers the owner-scoped contract of <see cref="ExternalServiceService"/> against a
/// real DbContext: the uniqueness rule, the cross-owner boundary, and the fact that
/// deleting a service cannot leave dangling dependencies behind.
/// </summary>
public sealed class ExternalServiceServiceTests
{
    private const string Owner = "owner-1";
    private const string OtherOwner = "owner-2";

    [Fact]
    public async Task Create_persists_the_service_and_reports_no_dependents()
    {
        using var context = CreateContext();
        var service = new ExternalServiceService(context, CurrentUser(Owner), TimeProvider.System);

        var created = await service.CreateAsync(new CreateExternalServiceDto
        {
            Name = "Stripe payments",
            Kind = "SaaS",
            Environment = "Production",
            Endpoint = "https://api.stripe.com",
            Provider = "Stripe, Inc.",
            Protocol = "HTTPS",
            Port = 443,
        });

        created.Should().NotBeNull();
        created!.Name.Should().Be("Stripe payments");
        created.DependentApplicationCount.Should().Be(0);

        var stored = await context.ExternalServices.SingleAsync();
        stored.Name.Should().Be("Stripe payments");
        stored.Port.Should().Be(443);
        stored.Endpoint.Should().Be("https://api.stripe.com");
    }

    [Fact]
    public async Task A_second_service_with_the_same_name_for_one_owner_is_refused()
    {
        using var context = CreateContext();
        var service = new ExternalServiceService(context, CurrentUser(Owner), TimeProvider.System);
        var request = new CreateExternalServiceDto { Name = "SMTP relay", Kind = "SMTP", Environment = "Production" };
        await service.CreateAsync(request);

        var duplicate = await service.CreateAsync(request);

        duplicate.Should().BeNull();
        (await context.ExternalServices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Two_owners_may_each_record_a_service_with_the_same_name()
    {
        using var context = CreateContext();
        var request = new CreateExternalServiceDto { Name = "Stripe", Kind = "SaaS", Environment = "Production" };

        var mine = await new ExternalServiceService(context, CurrentUser(Owner), TimeProvider.System).CreateAsync(request);
        var theirs = await new ExternalServiceService(context, CurrentUser(OtherOwner), TimeProvider.System).CreateAsync(request);

        mine.Should().NotBeNull();
        theirs.Should().NotBeNull();
        (await context.ExternalServices.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Listing_never_crosses_the_owner_boundary()
    {
        using var context = CreateContext();
        await new ExternalServiceService(context, CurrentUser(Owner), TimeProvider.System)
            .CreateAsync(new CreateExternalServiceDto { Name = "Mine", Kind = "API", Environment = "Production" });
        await new ExternalServiceService(context, CurrentUser(OtherOwner), TimeProvider.System)
            .CreateAsync(new CreateExternalServiceDto { Name = "Theirs", Kind = "API", Environment = "Production" });

        var listed = await new ExternalServiceService(context, CurrentUser(Owner), TimeProvider.System).ListAsync(null);

        listed.Should().ContainSingle().Which.Name.Should().Be("Mine");
    }

    [Fact]
    public async Task Reading_another_owners_service_returns_null()
    {
        using var context = CreateContext();
        var created = await new ExternalServiceService(context, CurrentUser(OtherOwner), TimeProvider.System)
            .CreateAsync(new CreateExternalServiceDto { Name = "Theirs", Kind = "API", Environment = "Production" });

        var fetched = await new ExternalServiceService(context, CurrentUser(Owner), TimeProvider.System)
            .GetAsync(created!.Id);

        fetched.Should().BeNull();
    }

    // Search runs through PostgreSQL ILIKE, so it is covered by
    // ExternalServiceSearchTests in AuditNode.Database.IntegrationTests.
    [Fact]
    public async Task Updating_renames_and_rejects_a_name_used_by_another_service()
    {
        using var context = CreateContext();
        var service = new ExternalServiceService(context, CurrentUser(Owner), TimeProvider.System);
        var first = await service.CreateAsync(new CreateExternalServiceDto { Name = "First", Kind = "API", Environment = "Production" });
        await service.CreateAsync(new CreateExternalServiceDto { Name = "Second", Kind = "API", Environment = "Production" });

        var renamed = await service.UpdateAsync(first!.Id, new UpdateExternalServiceDto { Name = "Renamed" });
        var conflict = await service.UpdateAsync(first.Id, new UpdateExternalServiceDto { Name = "Second" });

        renamed.Should().Be(ExternalServiceUpdateResult.Updated);
        conflict.Should().Be(ExternalServiceUpdateResult.NameConflict);
        (await context.ExternalServices.SingleAsync(item => item.Id == first.Id)).Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Updating_a_service_that_does_not_exist_reports_not_found()
    {
        using var context = CreateContext();
        var service = new ExternalServiceService(context, CurrentUser(Owner), TimeProvider.System);

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateExternalServiceDto { Name = "x" });

        result.Should().Be(ExternalServiceUpdateResult.NotFound);
    }

    [Fact]
    public async Task Dependent_application_count_reflects_distinct_applications()
    {
        using var context = CreateContext();
        var service = new ExternalServiceService(context, CurrentUser(Owner), TimeProvider.System);
        var created = await service.CreateAsync(new CreateExternalServiceDto { Name = "Stripe", Kind = "SaaS", Environment = "Production" });

        var sourceAppId = Guid.NewGuid();
        var otherAppId = Guid.NewGuid();
        context.AppDependencies.AddRange(
            new AppDependency
            {
                Id = Guid.NewGuid(), OwnerUserId = Owner, SourceAppId = sourceAppId,
                ExternalServiceId = created!.Id, ConnectionType = "HTTPS",
            },
            // Same source app, second dependency to the same service: counted once.
            new AppDependency
            {
                Id = Guid.NewGuid(), OwnerUserId = Owner, SourceAppId = sourceAppId,
                ExternalServiceId = created.Id, ConnectionType = "gRPC",
            },
            new AppDependency
            {
                Id = Guid.NewGuid(), OwnerUserId = Owner, SourceAppId = otherAppId,
                ExternalServiceId = created.Id, ConnectionType = "HTTPS",
            });
        await context.SaveChangesAsync();

        var fetched = await service.GetAsync(created.Id);

        fetched!.DependentApplicationCount.Should().Be(2);
    }

    [Fact]
    public async Task Deleting_a_service_removes_its_dependencies_and_is_owner_scoped()
    {
        using var context = CreateContext();
        var service = new ExternalServiceService(context, CurrentUser(Owner), TimeProvider.System);
        var created = await service.CreateAsync(new CreateExternalServiceDto { Name = "Stripe", Kind = "SaaS", Environment = "Production" });
        context.AppDependencies.Add(new AppDependency
        {
            Id = Guid.NewGuid(), OwnerUserId = Owner, SourceAppId = Guid.NewGuid(),
            ExternalServiceId = created!.Id, ConnectionType = "HTTPS",
        });
        await context.SaveChangesAsync();

        var deletedByStranger = await new ExternalServiceService(context, CurrentUser(OtherOwner), TimeProvider.System)
            .DeleteAsync(created.Id);
        var deleted = await service.DeleteAsync(created.Id);

        deletedByStranger.Should().BeFalse();
        deleted.Should().BeTrue();
        (await context.ExternalServices.CountAsync()).Should().Be(0);
        // The dependency cascades away with its service instead of dangling.
        (await context.AppDependencies.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_read_the_catalog()
    {
        using var context = CreateContext();
        var service = new ExternalServiceService(context, CurrentUser(null), TimeProvider.System);

        var act = () => service.ListAsync(null);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static AuditDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase($"external-services-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options);
    }

    private static ICurrentUserService CurrentUser(string? userId) => new StubCurrentUser(userId);

    private sealed class StubCurrentUser(string? userId) : ICurrentUserService
    {
        public string? UserId { get; } = userId;
    }
}

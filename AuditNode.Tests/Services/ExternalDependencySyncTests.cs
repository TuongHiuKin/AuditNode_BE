using AuditNode.Application.DTOs;
using AuditNode.Domain.Entities;
using AppEntity = AuditNode.Domain.Entities.Application;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

/// <summary>
/// Covers the external-service branch of dependency synchronization: an item may
/// target another application OR a recorded third-party service, never both and
/// never neither.
/// </summary>
public sealed class ExternalDependencySyncTests
{
    private const string Owner = "owner-external";

    [Fact]
    public async Task An_external_destination_is_persisted_without_an_internal_target()
    {
        await using var context = CreateContext();
        var source = App(Owner, "SRC");
        var external = External(Owner, "Stripe");
        context.AddRange(source, external);
        await context.SaveChangesAsync();

        var result = await Service(context, Owner).SyncDependenciesAsync(new SyncDependenciesDto
        {
            Version = 0,
            Dependencies = [new DependencyItemDto { SourceAppId = source.Id, ExternalServiceId = external.Id }],
        });

        result.Should().Be(DependencySyncStatus.Success);
        var stored = await context.AppDependencies.SingleAsync();
        stored.SourceAppId.Should().Be(source.Id);
        stored.ExternalServiceId.Should().Be(external.Id);
        stored.DestAppId.Should().BeNull();
        stored.DestPortId.Should().BeNull();
    }

    [Fact]
    public async Task Both_destinations_on_one_item_is_rejected()
    {
        await using var context = CreateContext();
        var source = App(Owner, "SRC");
        var destination = App(Owner, "DST");
        var mapping = Mapping(destination, Owner);
        var external = External(Owner, "Stripe");
        context.AddRange(source, destination, mapping, external);
        await context.SaveChangesAsync();

        var result = await Service(context, Owner).SyncDependenciesAsync(new SyncDependenciesDto
        {
            Version = 0,
            Dependencies =
            [
                new DependencyItemDto
                {
                    SourceAppId = source.Id,
                    DestAppId = destination.Id,
                    DestinationPortMappingId = mapping.Id,
                    ExternalServiceId = external.Id,
                },
            ],
        });

        result.Should().Be(DependencySyncStatus.InvalidRequest);
        (await context.AppDependencies.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_item_with_no_destination_is_rejected()
    {
        await using var context = CreateContext();
        var source = App(Owner, "SRC");
        context.Add(source);
        await context.SaveChangesAsync();

        var result = await Service(context, Owner).SyncDependenciesAsync(new SyncDependenciesDto
        {
            Version = 0,
            Dependencies = [new DependencyItemDto { SourceAppId = source.Id }],
        });

        result.Should().Be(DependencySyncStatus.InvalidRequest);
    }

    [Fact]
    public async Task Referencing_another_owners_external_service_is_not_found()
    {
        await using var context = CreateContext();
        var source = App(Owner, "SRC");
        var foreign = External("other-owner", "Their Stripe");
        context.AddRange(source, foreign);
        await context.SaveChangesAsync();

        var result = await Service(context, Owner).SyncDependenciesAsync(new SyncDependenciesDto
        {
            Version = 0,
            Dependencies = [new DependencyItemDto { SourceAppId = source.Id, ExternalServiceId = foreign.Id }],
        });

        result.Should().Be(DependencySyncStatus.NotFound);
        (await context.AppDependencies.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Internal_and_external_dependencies_coexist_in_one_sync()
    {
        await using var context = CreateContext();
        var source = App(Owner, "SRC");
        var destination = App(Owner, "DST");
        var mapping = Mapping(destination, Owner);
        var external = External(Owner, "Stripe");
        context.AddRange(source, destination, mapping, external);
        await context.SaveChangesAsync();

        var result = await Service(context, Owner).SyncDependenciesAsync(new SyncDependenciesDto
        {
            Version = 0,
            Dependencies =
            [
                new DependencyItemDto
                {
                    SourceAppId = source.Id,
                    DestAppId = destination.Id,
                    DestinationPortMappingId = mapping.Id,
                },
                new DependencyItemDto { SourceAppId = source.Id, ExternalServiceId = external.Id },
            ],
        });

        result.Should().Be(DependencySyncStatus.Success);
        var stored = await context.AppDependencies.ToListAsync();
        stored.Should().HaveCount(2);
        stored.Count(item => item.ExternalServiceId == external.Id).Should().Be(1);
        stored.Count(item => item.DestPortId == mapping.Id).Should().Be(1);
    }

    [Fact]
    public async Task Repeating_the_same_external_dependency_does_not_duplicate_it()
    {
        await using var context = CreateContext();
        var source = App(Owner, "SRC");
        var external = External(Owner, "Stripe");
        context.AddRange(source, external);
        await context.SaveChangesAsync();
        var request = new SyncDependenciesDto
        {
            Version = 0,
            Dependencies = [new DependencyItemDto { SourceAppId = source.Id, ExternalServiceId = external.Id }],
        };

        await Service(context, Owner).SyncDependenciesAsync(request);
        request.Version = 1;
        var second = await Service(context, Owner).SyncDependenciesAsync(request);

        second.Should().Be(DependencySyncStatus.Success);
        (await context.AppDependencies.CountAsync()).Should().Be(1);
    }

    private static AuditDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase($"external-dependency-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options);
    }

    private static DependencyService Service(AuditDbContext context, string userId)
    {
        var currentUser = new Mock<Application.Interfaces.ICurrentUserService>();
        currentUser.SetupGet(user => user.UserId).Returns(userId);
        return new DependencyService(context, NullLogger<DependencyService>.Instance, currentUser.Object);
    }

    private static AppEntity App(string owner, string code) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = owner,
        AppCode = code,
        AppName = code,
        OwnerTeam = "test",
        Risk = "Low",
        Icon = "app",
        TechStack = "test",
    };

    private static PortMapping Mapping(AppEntity app, string owner) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = owner,
        AppId = app.Id,
        ServerId = Guid.NewGuid(),
        PortNumber = 8080,
        Protocol = "TCP",
    };

    private static ExternalService External(string owner, string name) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = owner,
        Name = name,
        Kind = "SaaS",
        Environment = "Production",
    };
}

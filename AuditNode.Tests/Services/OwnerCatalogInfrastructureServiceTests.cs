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

public sealed class OwnerCatalogInfrastructureServiceTests
{
    private const string Owner = "owner-user";

    [Fact]
    public async Task Migrate_updates_mapping_only_after_transactional_permission_revalidation()
    {
        await using var context = CreateContext();
        var fixture = await SeedDeploymentAsync(context);
        var service = Service(context, AllowingCoordinator());

        var result = await service.MigrateAppAsync(new MigrateAppDto
        {
            PortMappingId = fixture.MappingId,
            TargetServerId = fixture.TargetServerId,
            NewPortNumber = 8443
        });

        result.Should().Be(DeploymentOperationStatus.Success);
        var mapping = await context.PortMappings.FindAsync(fixture.MappingId);
        mapping!.ServerId.Should().Be(fixture.TargetServerId);
        mapping.PortNumber.Should().Be(8443);
    }

    [Fact]
    public async Task Migrate_does_not_write_when_revalidation_observes_revoked_editor_grant()
    {
        await using var context = CreateContext();
        var fixture = await SeedDeploymentAsync(context);
        var coordinator = new Mock<ILabelMutationCoordinator>();
        coordinator.Setup(item => item.ExecuteAsync(
                Owner, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service(context, coordinator.Object).MigrateAppAsync(new MigrateAppDto
        {
            PortMappingId = fixture.MappingId,
            TargetServerId = fixture.TargetServerId,
            NewPortNumber = 8443
        });

        result.Should().Be(DeploymentOperationStatus.Forbidden);
        var mapping = await context.PortMappings.FindAsync(fixture.MappingId);
        mapping!.ServerId.Should().Be(fixture.SourceServerId);
        mapping.PortNumber.Should().Be(443);
    }

    [Fact]
    public async Task Migrate_returns_not_found_when_existing_mapping_is_outside_callers_read_scope()
    {
        await using var context = CreateContext();
        var fixture = await SeedDeploymentAsync(context);
        var access = new Mock<ILabelAccessService>();
        access.Setup(item => item.GetApplicationAccessAsync(fixture.ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceLabelAccessDto?)null);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(item => item.UserId).Returns("other-user");
        var service = new InfrastructureService(
            context,
            NullLogger<InfrastructureService>.Instance,
            access.Object,
            AllowingCoordinator(),
            currentUser.Object,
            Mock.Of<IGlobalCatalogRepository>(),
            TimeProvider.System);

        var result = await service.MigrateAppAsync(new MigrateAppDto
        {
            PortMappingId = fixture.MappingId,
            TargetServerId = fixture.TargetServerId,
            NewPortNumber = 8443
        });

        result.Should().Be(DeploymentOperationStatus.NotFound);
    }

    [Fact]
    public async Task Owner_purge_removes_application_deployments_and_incident_dependencies()
    {
        await using var context = CreateContext();
        var fixture = await SeedDeploymentAsync(context);
        var otherAppId = Guid.NewGuid();
        var otherMappingId = Guid.NewGuid();
        context.Applications.Add(new AppEntity
        {
            Id = otherAppId, OwnerUserId = Owner, AppCode = "OTHER", AppName = "Other", OwnerTeam = "Team"
        });
        context.PortMappings.Add(new PortMapping
        {
            Id = otherMappingId, OwnerUserId = Owner, AppId = otherAppId,
            ServerId = fixture.TargetServerId, PortNumber = 9443, Protocol = "HTTPS"
        });
        context.AppDependencies.Add(new AppDependency
        {
            Id = Guid.NewGuid(), OwnerUserId = Owner, SourceAppId = fixture.ApplicationId,
            DestAppId = otherAppId, DestPortId = otherMappingId, ConnectionType = "HTTPS"
        });
        await context.SaveChangesAsync();

        var result = await Service(context, AllowingCoordinator()).PurgeAppAsync(fixture.ApplicationId);

        result.Should().BeTrue();
        (await context.Applications.FindAsync(fixture.ApplicationId)).Should().BeNull();
        (await context.PortMappings.AnyAsync(item => item.AppId == fixture.ApplicationId)).Should().BeFalse();
        (await context.AppDependencies.AnyAsync(item => item.SourceAppId == fixture.ApplicationId || item.DestAppId == fixture.ApplicationId)).Should().BeFalse();
        (await context.Applications.FindAsync(otherAppId)).Should().NotBeNull();
    }

    private static InfrastructureService Service(AuditDbContext context, ILabelMutationCoordinator coordinator)
    {
        var access = new Mock<ILabelAccessService>();
        var ownerAccess = new ResourceLabelAccessDto(Guid.Empty, Owner, LabelEffectivePermission.Owner, [], new(true, true, true, true, true, false, true));
        access.Setup(item => item.GetApplicationAccessAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => ownerAccess with { ResourceId = id });
        access.Setup(item => item.GetServerAccessAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => ownerAccess with { ResourceId = id });
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(item => item.UserId).Returns(Owner);
        return new InfrastructureService(
            context,
            NullLogger<InfrastructureService>.Instance,
            access.Object,
            coordinator,
            currentUser.Object,
            Mock.Of<IGlobalCatalogRepository>(),
            TimeProvider.System);
    }

    private static ILabelMutationCoordinator AllowingCoordinator()
    {
        var coordinator = new Mock<ILabelMutationCoordinator>();
        coordinator.Setup(item => item.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, IReadOnlyCollection<Guid> _, IReadOnlyCollection<Guid> _, Func<CancellationToken, Task> mutation, CancellationToken cancellationToken) =>
            {
                await mutation(cancellationToken);
                return true;
            });
        return coordinator.Object;
    }

    private static AuditDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options);
    }

    private static async Task<DeploymentFixture> SeedDeploymentAsync(AuditDbContext context)
    {
        var datacenterId = Guid.NewGuid();
        var sourceServerId = Guid.NewGuid();
        var targetServerId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var mappingId = Guid.NewGuid();
        context.Datacenters.Add(new Datacenter { Id = datacenterId, OwnerUserId = Owner, Name = "DC", Location = "VN" });
        context.Servers.AddRange(
            new Server { Id = sourceServerId, OwnerUserId = Owner, DatacenterId = datacenterId, Hostname = "source", IpAddress = "10.0.0.1", OsType = "Linux", Environment = "Production", Status = "Active" },
            new Server { Id = targetServerId, OwnerUserId = Owner, DatacenterId = datacenterId, Hostname = "target", IpAddress = "10.0.0.2", OsType = "Linux", Environment = "Production", Status = "Active" });
        context.Applications.Add(new AppEntity { Id = applicationId, OwnerUserId = Owner, AppCode = "APP", AppName = "App", OwnerTeam = "Team" });
        context.PortMappings.Add(new PortMapping { Id = mappingId, OwnerUserId = Owner, AppId = applicationId, ServerId = sourceServerId, PortNumber = 443, Protocol = "HTTPS" });
        await context.SaveChangesAsync();
        return new(applicationId, mappingId, sourceServerId, targetServerId);
    }

    private sealed record DeploymentFixture(Guid ApplicationId, Guid MappingId, Guid SourceServerId, Guid TargetServerId);
}

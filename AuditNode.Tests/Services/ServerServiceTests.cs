using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using Xunit;

namespace AuditNode.Tests.Services;

public class ServerServiceTests
{
    private readonly Mock<IServerRepository> _repository = new();
    [Fact]
    public async Task Create_rejects_datacenter_not_visible_in_current_workspace()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId, "test-user")).ReturnsAsync(false);

        var result = await Service().CreateServerAsync(dto);

        result.Status.Should().Be(ServerOperationStatus.DatacenterNotFound);
        _repository.Verify(x => x.CreateServerAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>>()), Times.Never);
    }

    [Fact]
    public async Task Create_rejects_duplicate_ip_in_current_workspace()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId, "test-user")).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(dto.IpAddress, "test-user", null)).ReturnsAsync(true);

        var result = await Service().CreateServerAsync(dto);

        result.Status.Should().Be(ServerOperationStatus.DuplicateIp);
    }

    [Fact]
    public async Task Create_maps_entity_and_returns_created_server()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId, "test-user")).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(dto.IpAddress, "test-user", null)).ReturnsAsync(false);
        _repository.Setup(x => x.CreateServerAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>>()))
            .ReturnsAsync((Server value, IReadOnlyCollection<LabelDto> labels) => value);

        var result = await Service().CreateServerAsync(dto);

        result.Status.Should().Be(ServerOperationStatus.Success);
        result.Server.Should().NotBeNull();
        result.Server!.Id.Should().NotBe(Guid.Empty);
        result.Server.IpAddress.Should().Be(dto.IpAddress);
        result.Server.OwnerUserId.Should().Be("test-user");
        _repository.Verify(x => x.CreateServerAsync(It.Is<Server>(s =>
            s.Id != Guid.Empty && s.DatacenterId == dto.DatacenterId && s.IpAddress == dto.IpAddress && s.OwnerUserId == "test-user"), dto.Labels));
    }

    [Fact]
    public async Task Update_excludes_current_server_when_checking_duplicate_ip()
    {
        var id = Guid.NewGuid();
        var dto = ValidUpdate();
        var existing = new Server { Id = id, OwnerUserId = "test-user", DatacenterId = Guid.NewGuid(), IpAddress = "10.0.0.1" };
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(existing);
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId, "test-user")).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(dto.IpAddress, "test-user", id)).ReturnsAsync(false);

        var result = await Service().UpdateServerAsync(id, dto);

        result.Status.Should().Be(ServerOperationStatus.Success);
        existing.IpAddress.Should().Be(dto.IpAddress);
        _repository.Verify(x => x.UpdateAsync(existing, dto.Labels), Times.Once);
    }

    [Fact]
    public async Task Unique_constraint_race_is_reported_as_conflict()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId, "test-user")).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(dto.IpAddress, "test-user", null)).ReturnsAsync(false);
        _repository.Setup(x => x.CreateServerAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>>()))
            .ThrowsAsync(new DbUpdateException(
                "save failed",
                new PostgresException("duplicate", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)));

        var result = await Service().CreateServerAsync(dto);

        result.Status.Should().Be(ServerOperationStatus.DuplicateIp);
    }

    [Fact]
    public async Task Non_unique_database_failure_is_not_misreported_as_duplicate_ip()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId, "test-user")).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(dto.IpAddress, "test-user", null)).ReturnsAsync(false);
        _repository.Setup(x => x.CreateServerAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>>()))
            .ThrowsAsync(new DbUpdateException("database unavailable"));

        var action = () => Service().CreateServerAsync(dto);

        await action.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Purge_removes_only_server_visible_to_current_tenant()
    {
        var id = Guid.NewGuid();
        var existing = new Server { Id = id, OwnerUserId = "test-user" };
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(existing);

        var result = await Service().PurgeServerAsync(id);

        result.Should().Be(ServerOperationStatus.Success);
        _repository.Verify(x => x.DeleteAsync(existing), Times.Once);
    }

    [Fact]
    public async Task Export_deduplicates_ids_and_removes_empty_values()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var catalog = new Mock<IGlobalCatalogRepository>();
        catalog.Setup(x => x.ExportServersAsync("test-user", CatalogView.Mine, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await Service(catalog.Object).ExportServersAsync([first, second, first, Guid.Empty]);

        catalog.Verify(x => x.ExportServersAsync("test-user", CatalogView.Mine, It.Is<IReadOnlyCollection<Guid>>(ids =>
            ids.Order().SequenceEqual(new[] { first, second }.Order())), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private ServerService Service(IGlobalCatalogRepository? catalog = null)
    {
        var access = new Mock<ILabelAccessService>();
        access.Setup(x => x.GetServerAccessAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(
            (Guid id, CancellationToken _) => new ResourceLabelAccessDto(id, "test-user", LabelEffectivePermission.Owner, [], new(true, true, true, true, true, false, true)));
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("test-user");
        return new(_repository.Object, access.Object, AllowingCoordinator(), user.Object, catalog ?? Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);
    }

    private static ILabelMutationCoordinator AllowingCoordinator()
    {
        var coordinator = new Mock<ILabelMutationCoordinator>();
        coordinator.Setup(item => item.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, IReadOnlyCollection<Guid> _, IReadOnlyCollection<Guid> _, Func<CancellationToken, Task> mutation, CancellationToken cancellationToken) =>
            {
                await mutation(cancellationToken);
                return true;
            });
        return coordinator.Object;
    }

    [Fact]
    public async Task Create_ShouldFailClosed_WhenCurrentUserIsUnavailable()
    {
        var access = new Mock<ILabelAccessService>();
        var user = new Mock<ICurrentUserService>();
        var service = new ServerService(_repository.Object, access.Object, AllowingCoordinator(), user.Object, Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);

        var result = await service.CreateServerAsync(ValidCreate());

        result.Status.Should().Be(ServerOperationStatus.Forbidden);
        _repository.Verify(x => x.CreateServerAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>>()), Times.Never);
    }

    [Fact]
    public async Task Update_ShouldRejectLabelsThatWouldEscapeAuditorScope()
    {
        var id = Guid.NewGuid();
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(new Server { Id = id, OwnerUserId = "owner" });
        var access = new Mock<ILabelAccessService>();
        access.Setup(x => x.GetServerAccessAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ResourceLabelAccessDto(id, "owner", LabelEffectivePermission.Editor, [], new(true, true, false, false, false, false, false)));
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("auditor");
        var service = new ServerService(_repository.Object, access.Object, AllowingCoordinator(), user.Object, Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);
        var dto = ValidUpdate();
        dto.Labels = [new LabelDto { Key = "env", Value = "production" }];

        var result = await service.UpdateServerAsync(id, dto);

        result.Status.Should().Be(ServerOperationStatus.Forbidden);
        _repository.Verify(x => x.UpdateAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>?>()), Times.Never);
    }

    [Fact]
    public async Task Editor_can_update_properties_inside_granted_label_but_labels_are_preserved()
    {
        var id = Guid.NewGuid();
        var existing = new Server { Id = id, OwnerUserId = "owner", DatacenterId = Guid.NewGuid(), IpAddress = "10.0.0.1" };
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(existing);
        _repository.Setup(x => x.DatacenterExistsAsync(It.IsAny<Guid>(), "owner")).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(It.IsAny<string>(), "owner", id)).ReturnsAsync(false);
        var access = new Mock<ILabelAccessService>();
        access.Setup(x => x.GetServerAccessAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ResourceLabelAccessDto(id, "owner", LabelEffectivePermission.Editor, [Guid.NewGuid()], new(true, true, false, false, false, false, false)));
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("editor");
        var service = new ServerService(_repository.Object, access.Object, AllowingCoordinator(), user.Object, Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);
        var dto = ValidUpdate();
        dto.Labels = null;

        var result = await service.UpdateServerAsync(id, dto);

        result.Status.Should().Be(ServerOperationStatus.Success);
        _repository.Verify(x => x.UpdateAsync(existing, null), Times.Once);
    }

    [Fact]
    public async Task Editor_update_fails_when_transactional_revalidation_observes_revoke()
    {
        var id = Guid.NewGuid();
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(new Server { Id = id, OwnerUserId = "owner" });
        _repository.Setup(x => x.DatacenterExistsAsync(It.IsAny<Guid>(), "owner")).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(It.IsAny<string>(), "owner", id)).ReturnsAsync(false);
        var access = new Mock<ILabelAccessService>();
        access.Setup(x => x.GetServerAccessAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ResourceLabelAccessDto(id, "owner", LabelEffectivePermission.Editor, [Guid.NewGuid()], new(true, true, false, false, false, false, false)));
        var coordinator = new Mock<ILabelMutationCoordinator>();
        coordinator.Setup(item => item.ExecuteAsync(
                "owner", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = new ServerService(
            _repository.Object, access.Object, coordinator.Object, Mock.Of<ICurrentUserService>(),
            Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);
        var dto = ValidUpdate();
        dto.Labels = null;

        var result = await service.UpdateServerAsync(id, dto);

        result.Status.Should().Be(ServerOperationStatus.Forbidden);
        _repository.Verify(x => x.UpdateAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>?>()), Times.Never);
    }

    [Fact]
    public async Task Viewer_cannot_update_shared_server_properties()
    {
        var id = Guid.NewGuid();
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(new Server { Id = id, OwnerUserId = "owner" });
        var access = new Mock<ILabelAccessService>();
        access.Setup(x => x.GetServerAccessAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ResourceLabelAccessDto(id, "owner", LabelEffectivePermission.Viewer, [Guid.NewGuid()], new(true, false, false, false, false, false, false)));
        var service = new ServerService(_repository.Object, access.Object, AllowingCoordinator(), Mock.Of<ICurrentUserService>(), Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);

        var result = await service.UpdateServerAsync(id, ValidUpdate());

        result.Status.Should().Be(ServerOperationStatus.Forbidden);
        _repository.Verify(x => x.UpdateAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>?>()), Times.Never);
    }

    private static CreateServerDto ValidCreate() => new()
    {
        DatacenterId = Guid.NewGuid(),
        IpAddress = "10.20.30.40",
        Hostname = "srv-01",
        OsType = "Linux",
        Environment = "Production",
        Status = "Active",
        Labels = []
    };

    private static UpdateServerDto ValidUpdate() => new()
    {
        DatacenterId = Guid.NewGuid(),
        IpAddress = "10.20.30.41",
        Hostname = "srv-01-updated",
        OsType = "Linux",
        Environment = "Production",
        Status = "Active",
        Labels = []
    };
}

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
    private readonly Mock<ITenantProvider> _tenant = new();

    public ServerServiceTests()
    {
        _tenant.SetupGet(x => x.WorkspaceId).Returns(Guid.NewGuid());
    }

    [Fact]
    public async Task Create_rejects_missing_tenant_without_calling_repository()
    {
        _tenant.SetupGet(x => x.WorkspaceId).Returns((Guid?)null);

        var result = await Service().CreateServerAsync(ValidCreate());

        result.Status.Should().Be(ServerOperationStatus.InvalidWorkspace);
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_rejects_datacenter_not_visible_in_current_workspace()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId)).ReturnsAsync(false);

        var result = await Service().CreateServerAsync(dto);

        result.Status.Should().Be(ServerOperationStatus.DatacenterNotFound);
        _repository.Verify(x => x.CreateServerAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>>()), Times.Never);
    }

    [Fact]
    public async Task Create_rejects_duplicate_ip_in_current_workspace()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId)).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(dto.IpAddress, null)).ReturnsAsync(true);

        var result = await Service().CreateServerAsync(dto);

        result.Status.Should().Be(ServerOperationStatus.DuplicateIp);
    }

    [Fact]
    public async Task Create_maps_entity_and_returns_created_server()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId)).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(dto.IpAddress, null)).ReturnsAsync(false);
        _repository.Setup(x => x.CreateServerAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>>()))
            .ReturnsAsync((Server value, IReadOnlyCollection<LabelDto> labels) => value);

        var result = await Service().CreateServerAsync(dto);

        result.Status.Should().Be(ServerOperationStatus.Success);
        result.Server.Should().NotBeNull();
        result.Server!.Id.Should().NotBe(Guid.Empty);
        result.Server.IpAddress.Should().Be(dto.IpAddress);
        _repository.Verify(x => x.CreateServerAsync(It.Is<Server>(s =>
            s.Id != Guid.Empty && s.DatacenterId == dto.DatacenterId && s.IpAddress == dto.IpAddress), dto.Labels));
    }

    [Fact]
    public async Task Update_excludes_current_server_when_checking_duplicate_ip()
    {
        var id = Guid.NewGuid();
        var dto = ValidUpdate();
        var existing = new Server { Id = id, DatacenterId = Guid.NewGuid(), IpAddress = "10.0.0.1" };
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(existing);
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId)).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(dto.IpAddress, id)).ReturnsAsync(false);

        var result = await Service().UpdateServerAsync(id, dto);

        result.Status.Should().Be(ServerOperationStatus.Success);
        existing.IpAddress.Should().Be(dto.IpAddress);
        _repository.Verify(x => x.UpdateAsync(existing, dto.Labels), Times.Once);
    }

    [Fact]
    public async Task Unique_constraint_race_is_reported_as_conflict()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId)).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(dto.IpAddress, null)).ReturnsAsync(false);
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
        _repository.Setup(x => x.DatacenterExistsAsync(dto.DatacenterId)).ReturnsAsync(true);
        _repository.Setup(x => x.IpAddressExistsAsync(dto.IpAddress, null)).ReturnsAsync(false);
        _repository.Setup(x => x.CreateServerAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>>()))
            .ThrowsAsync(new DbUpdateException("database unavailable"));

        var action = () => Service().CreateServerAsync(dto);

        await action.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Purge_removes_only_server_visible_to_current_tenant()
    {
        var id = Guid.NewGuid();
        var existing = new Server { Id = id };
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
        _repository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(Array.Empty<ServerResponseDto>());

        await Service().ExportServersAsync([first, second, first, Guid.Empty]);

        _repository.Verify(x => x.GetScopedAsync(null, null, It.Is<IEnumerable<Guid>>(ids =>
            ids.Order().SequenceEqual(new[] { first, second }.Order()))), Times.Once);
    }

    private ServerService Service()
    {
        var policy = new Mock<IScopedResourcePolicy>();
        policy.Setup(x => x.CanReadAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        policy.Setup(x => x.CanWriteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        policy.Setup(x => x.CanCreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<LabelDto>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        policy.Setup(x => x.GetReadableIdsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlySet<Guid>?)null);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("test-user");
        return new(_repository.Object, _tenant.Object, policy.Object, user.Object);
    }

    [Fact]
    public async Task Create_ShouldFailClosed_WhenCurrentUserIsUnavailable()
    {
        var policy = new Mock<IScopedResourcePolicy>();
        var user = new Mock<ICurrentUserService>();
        var service = new ServerService(_repository.Object, _tenant.Object, policy.Object, user.Object);

        var result = await service.CreateServerAsync(ValidCreate());

        result.Status.Should().Be(ServerOperationStatus.Forbidden);
        _repository.Verify(x => x.CreateServerAsync(It.IsAny<Server>(), It.IsAny<IReadOnlyCollection<LabelDto>>()), Times.Never);
    }

    [Fact]
    public async Task Update_ShouldRejectLabelsThatWouldEscapeAuditorScope()
    {
        var id = Guid.NewGuid();
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(new Server { Id = id });
        var policy = new Mock<IScopedResourcePolicy>();
        policy.Setup(x => x.CanWriteAsync(It.IsAny<Guid>(), "auditor", "server", id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        policy.Setup(x => x.CanCreateAsync(It.IsAny<Guid>(), "auditor", "server", It.IsAny<IReadOnlyCollection<LabelDto>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("auditor");
        var service = new ServerService(_repository.Object, _tenant.Object, policy.Object, user.Object);
        var dto = ValidUpdate();
        dto.Labels = [new LabelDto { Key = "env", Value = "production" }];

        var result = await service.UpdateServerAsync(id, dto);

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

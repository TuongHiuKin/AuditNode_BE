using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Moq;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Services;

public class ApplicationServiceTests
{
    private readonly Mock<IApplicationRepository> _repository = new();
    [Fact]
    public async Task Create_rejects_duplicate_app_code_in_owner_catalog()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.AppCodeExistsAsync("APP01", "test-user", null)).ReturnsAsync(true);

        var result = await Service().CreateAsync(dto);

        result.Status.Should().Be(ApplicationOperationStatus.DuplicateAppCode);
        _repository.Verify(x => x.CreateAsync(
            It.IsAny<AppEntity>(), It.IsAny<IReadOnlyCollection<LabelDto>>(), It.IsAny<PortMapping?>()), Times.Never);
    }

    [Fact]
    public async Task Create_with_deployment_rejects_server_outside_owner_catalog()
    {
        var dto = ValidCreate();
        dto.Deployment = new CreateApplicationDeploymentDto
        {
            ServerId = Guid.NewGuid(), PortNumber = 443, Protocol = "TCP"
        };
        var result = await Service().CreateAsync(dto);

        result.Status.Should().Be(ApplicationOperationStatus.ServerNotFound);
    }

    [Fact]
    public async Task Create_persists_labels_and_optional_deployment_in_one_repository_call()
    {
        var dto = ValidCreate();
        dto.Labels = [new LabelDto { Key = "tier", Value = "critical" }];
        dto.Deployment = new CreateApplicationDeploymentDto
        {
            ServerId = Guid.NewGuid(), PortNumber = 443, Protocol = "tcp"
        };
        _repository.Setup(x => x.ServerExistsAsync(dto.Deployment.ServerId, "test-user")).ReturnsAsync(true);
        _repository.Setup(x => x.CreateAsync(
                It.IsAny<AppEntity>(), It.IsAny<IReadOnlyCollection<LabelDto>>(), It.IsAny<PortMapping>()))
            .ReturnsAsync((AppEntity app, IReadOnlyCollection<LabelDto> _, PortMapping? _) => app);
        _repository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((AppEntity?)null);

        var result = await Service().CreateAsync(dto);

        result.Status.Should().Be(ApplicationOperationStatus.Success);
        _repository.Verify(x => x.CreateAsync(
            It.Is<AppEntity>(app => app.AppCode == "APP01" && app.OwnerUserId == "test-user"),
            It.Is<IReadOnlyCollection<LabelDto>>(labels => labels.Count == 1),
            It.Is<PortMapping>(mapping => mapping.Id != Guid.Empty && mapping.ServerId == dto.Deployment.ServerId && mapping.OwnerUserId == "test-user" &&
                                          mapping.PortNumber == 443 && mapping.Protocol == "TCP")), Times.Once);
    }

    [Fact]
    public async Task Metadata_only_update_never_selects_or_migrates_a_deployment()
    {
        var id = Guid.NewGuid();
        var app = new AppEntity { Id = id, OwnerUserId = "test-user", AppCode = "APP01" };
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(app);
        var dto = ValidUpdate();

        var result = await Service().UpdateAsync(id, dto);

        result.Status.Should().Be(ApplicationOperationStatus.Success);
        _repository.Verify(x => x.GetPortMappingAsync(It.IsAny<Guid>()), Times.Never);
        _repository.Verify(x => x.UpdateAsync(app, dto.Labels, null), Times.Once);
    }

    [Fact]
    public async Task Deployment_update_requires_mapping_owned_by_application()
    {
        var id = Guid.NewGuid();
        var dto = ValidUpdate();
        dto.PortMappingId = Guid.NewGuid();
        dto.TargetServerId = Guid.NewGuid();
        dto.PortNumber = 443;
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(new AppEntity { Id = id, OwnerUserId = "test-user" });
        _repository.Setup(x => x.GetPortMappingAsync(dto.PortMappingId.Value))
            .ReturnsAsync(new PortMapping { Id = dto.PortMappingId.Value, AppId = Guid.NewGuid() });

        var result = await Service().UpdateAsync(id, dto);

        result.Status.Should().Be(ApplicationOperationStatus.DeploymentNotFound);
    }

    [Fact]
    public async Task Editor_can_update_application_properties_without_changing_labels()
    {
        var id = Guid.NewGuid();
        var application = new AppEntity { Id = id, OwnerUserId = "owner", AppCode = "APP01" };
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(application);
        var access = new Mock<ILabelAccessService>();
        access.Setup(x => x.GetApplicationAccessAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ResourceLabelAccessDto(id, "owner", LabelEffectivePermission.Editor, [Guid.NewGuid()], new(true, true, false, false, false, false, false)));
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("editor");
        var service = new ApplicationService(_repository.Object, access.Object, AllowingCoordinator(), user.Object, Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);
        var dto = ValidUpdate();
        dto.Labels = null;

        var result = await service.UpdateAsync(id, dto);

        result.Status.Should().Be(ApplicationOperationStatus.Success);
        _repository.Verify(x => x.UpdateAsync(application, null, null), Times.Once);
    }

    [Fact]
    public async Task Editor_update_fails_when_transactional_revalidation_observes_revoke()
    {
        var id = Guid.NewGuid();
        var application = new AppEntity { Id = id, OwnerUserId = "owner", AppCode = "APP01" };
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(application);
        var access = new Mock<ILabelAccessService>();
        access.Setup(x => x.GetApplicationAccessAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ResourceLabelAccessDto(id, "owner", LabelEffectivePermission.Editor, [Guid.NewGuid()], new(true, true, false, false, false, false, false)));
        var coordinator = new Mock<ILabelMutationCoordinator>();
        coordinator.Setup(item => item.ExecuteAsync(
                "owner", It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = new ApplicationService(
            _repository.Object, access.Object, coordinator.Object, Mock.Of<ICurrentUserService>(),
            Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);
        var dto = ValidUpdate();
        dto.Labels = null;

        var result = await service.UpdateAsync(id, dto);

        result.Status.Should().Be(ApplicationOperationStatus.Forbidden);
        _repository.Verify(x => x.UpdateAsync(It.IsAny<AppEntity>(), It.IsAny<IReadOnlyCollection<LabelDto>?>(), It.IsAny<PortMapping?>()), Times.Never);
    }

    private ApplicationService Service()
    {
        var access = new Mock<ILabelAccessService>();
        access.Setup(x => x.GetApplicationAccessAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(
            (Guid id, CancellationToken _) => new ResourceLabelAccessDto(id, "test-user", LabelEffectivePermission.Owner, [], new(true, true, true, true, true, false, true)));
        access.Setup(x => x.GetServerAccessAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(
            (Guid id, CancellationToken _) => new ResourceLabelAccessDto(id, "test-user", LabelEffectivePermission.Owner, [], new(true, true, true, true, true, false, true)));
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("test-user");
        return new(_repository.Object, access.Object, AllowingCoordinator(), user.Object, Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);
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

    private static CreateApplicationDto ValidCreate() => new()
    {
        AppCode = "app01", AppName = "App", OwnerTeam = "Team"
    };

    private static UpdateApplicationDto ValidUpdate() => new()
    {
        AppName = "App", OwnerTeam = "Team", Risk = "LOW", Icon = "", TechStack = "", Labels = []
    };
}

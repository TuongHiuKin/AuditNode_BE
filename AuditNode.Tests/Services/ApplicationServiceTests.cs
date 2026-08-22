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
    private readonly Mock<ITenantProvider> _tenant = new();

    public ApplicationServiceTests() =>
        _tenant.SetupGet(x => x.WorkspaceId).Returns(Guid.NewGuid());

    [Fact]
    public async Task Create_rejects_duplicate_app_code_in_workspace()
    {
        var dto = ValidCreate();
        _repository.Setup(x => x.AppCodeExistsAsync("APP01", null)).ReturnsAsync(true);

        var result = await Service().CreateAsync(dto);

        result.Status.Should().Be(ApplicationOperationStatus.DuplicateAppCode);
        _repository.Verify(x => x.CreateAsync(
            It.IsAny<AppEntity>(), It.IsAny<IReadOnlyCollection<LabelDto>>(), It.IsAny<PortMapping?>()), Times.Never);
    }

    [Fact]
    public async Task Create_with_deployment_rejects_server_outside_workspace()
    {
        var dto = ValidCreate();
        dto.Deployment = new CreateApplicationDeploymentDto
        {
            ServerId = Guid.NewGuid(), PortNumber = 443, Protocol = "TCP"
        };
        _repository.Setup(x => x.ServerExistsAsync(dto.Deployment.ServerId)).ReturnsAsync(false);

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
        _repository.Setup(x => x.ServerExistsAsync(dto.Deployment.ServerId)).ReturnsAsync(true);
        _repository.Setup(x => x.CreateAsync(
                It.IsAny<AppEntity>(), It.IsAny<IReadOnlyCollection<LabelDto>>(), It.IsAny<PortMapping>()))
            .ReturnsAsync((AppEntity app, IReadOnlyCollection<LabelDto> _, PortMapping? _) => app);
        _repository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((AppEntity?)null);

        var result = await Service().CreateAsync(dto);

        result.Status.Should().Be(ApplicationOperationStatus.Success);
        _repository.Verify(x => x.CreateAsync(
            It.Is<AppEntity>(app => app.AppCode == "APP01"),
            It.Is<IReadOnlyCollection<LabelDto>>(labels => labels.Count == 1),
            It.Is<PortMapping>(mapping => mapping.Id != Guid.Empty && mapping.ServerId == dto.Deployment.ServerId &&
                                          mapping.PortNumber == 443 && mapping.Protocol == "TCP")), Times.Once);
    }

    [Fact]
    public async Task Metadata_only_update_never_selects_or_migrates_a_deployment()
    {
        var id = Guid.NewGuid();
        var app = new AppEntity { Id = id, AppCode = "APP01" };
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
        _repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(new AppEntity { Id = id });
        _repository.Setup(x => x.GetPortMappingAsync(dto.PortMappingId.Value))
            .ReturnsAsync(new PortMapping { Id = dto.PortMappingId.Value, AppId = Guid.NewGuid() });

        var result = await Service().UpdateAsync(id, dto);

        result.Status.Should().Be(ApplicationOperationStatus.DeploymentNotFound);
    }

    private ApplicationService Service() => new(_repository.Object, _tenant.Object);

    private static CreateApplicationDto ValidCreate() => new()
    {
        AppCode = "app01", AppName = "App", OwnerTeam = "Team"
    };

    private static UpdateApplicationDto ValidUpdate() => new()
    {
        AppName = "App", OwnerTeam = "Team", Risk = "LOW", Icon = "", TechStack = "", Labels = []
    };
}

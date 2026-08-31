using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class ApplicationsControllerTests
{
    private readonly Mock<IApplicationService> _service = new();

    [Fact]
    public async Task Create_duplicate_code_returns_conflict()
    {
        var dto = new CreateApplicationDto();
        _service.Setup(x => x.CreateAsync(dto))
            .ReturnsAsync(new ApplicationOperationResult(ApplicationOperationStatus.DuplicateAppCode));

        var result = await Controller().PostApplication(dto);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Update_port_collision_returns_conflict()
    {
        var id = Guid.NewGuid();
        var dto = new UpdateApplicationDto();
        _service.Setup(x => x.UpdateAsync(id, dto))
            .ReturnsAsync(new ApplicationOperationResult(ApplicationOperationStatus.PortCollision));

        var result = await Controller().PutApplication(id, dto);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Get_passes_label_filters_to_service()
    {
        _service.Setup(x => x.GetCatalogPageAsync(
                It.Is<CatalogPageQuery>(query => query.View == CatalogView.Mine && query.Limit == 25),
                "tier", "critical", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorPageDto<ApplicationResponseDto>([], null, false));

        await Controller().GetApplications("tier", "critical");

        _service.Verify(x => x.GetCatalogPageAsync(
            It.Is<CatalogPageQuery>(query => query.View == CatalogView.Mine && query.Limit == 25),
            "tier", "critical", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unexpected_error_returns_safe_500()
    {
        const string secret = "database password";
        _service.Setup(x => x.GetCatalogDetailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(secret));

        var result = await Controller().GetApplication(Guid.NewGuid());

        var failure = result.Result.Should().BeOfType<ObjectResult>().Subject;
        failure.StatusCode.Should().Be(500);
        failure.Value!.ToString().Should().NotContain(secret);
    }

    private ApplicationsController Controller() => new(_service.Object);
}

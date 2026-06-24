using AuditNode.Application.Interfaces;
using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class ApplicationsControllerTests
{
    private readonly Mock<IApplicationService> _mockService;
    private readonly ApplicationsController _controller;

    public ApplicationsControllerTests()
    {
        _mockService = new Mock<IApplicationService>();
        _controller = new ApplicationsController(_mockService.Object);
    }

    [Fact]
    public async Task ExportApplications_ShouldReturnOk_WithSelectedApps()
    {
        // Arrange
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var mockApps = new List<ApplicationResponseDto>
        {
            new ApplicationResponseDto { Id = ids[0], AppName = "App 1" },
            new ApplicationResponseDto { Id = ids[1], AppName = "App 2" }
        };

        _mockService.Setup(s => s.GetByIdsAsync(ids))
            .ReturnsAsync(mockApps);

        // Act
        var result = await _controller.ExportApplications(ids);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.Should().BeEquivalentTo(mockApps);
    }

    [Fact]
    public async Task ExportApplications_ShouldReturnBadRequest_WhenNoIdsProvided()
    {
        // Act
        var result = await _controller.ExportApplications(null!);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}

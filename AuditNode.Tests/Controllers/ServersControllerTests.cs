using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class ServersControllerTests
{
    private readonly Mock<IServerService> _mockService;
    private readonly ServersController _controller;

    public ServersControllerTests()
    {
        _mockService = new Mock<IServerService>();
        _controller = new ServersController(_mockService.Object);
    }

    [Fact]
    public async Task ExportServers_ShouldReturnOk_WithSelectedServers()
    {
        // Arrange
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var mockServers = new List<ServerResponseDto>
        {
            new ServerResponseDto { Id = ids[0], Hostname = "S1" },
            new ServerResponseDto { Id = ids[1], Hostname = "S2" }
        };

        _mockService.Setup(s => s.GetByIdsAsync(ids))
            .ReturnsAsync(mockServers);

        // Act
        var result = await _controller.ExportServers(ids);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.Should().BeEquivalentTo(mockServers);
    }

    [Fact]
    public async Task ExportServers_ShouldReturnBadRequest_WhenNoIdsProvided()
    {
        // Act
        var result = await _controller.ExportServers(null!);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Application.Services;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Services;

public class ApplicationServiceTests
{
    private readonly Mock<IApplicationRepository> _repositoryMock;
    private readonly ApplicationService _service;

    public ApplicationServiceTests()
    {
        _repositoryMock = new Mock<IApplicationRepository>();
        _service = new ApplicationService(_repositoryMock.Object);
    }

    [Fact]
    public async Task UpdateAsync_ShouldCallRepository_WithNetworkUpdate()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var updateDto = new UpdateApplicationDto { AppName = "New Name", OwnerTeam = "New Team" };

        _repositoryMock.Setup(r => r.UpdateApplicationWithNetworkAsync(appId, updateDto))
            .ReturnsAsync(true);

        // Act
        var result = await _service.UpdateAsync(appId, updateDto);

        // Assert
        result.Should().BeTrue();
        _repositoryMock.Verify(r => r.UpdateApplicationWithNetworkAsync(appId, updateDto), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturnFalse_WhenRepositoryReturnsFalse()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var updateDto = new UpdateApplicationDto { AppName = "New Name" };

        _repositoryMock.Setup(r => r.UpdateApplicationWithNetworkAsync(appId, updateDto))
            .ReturnsAsync(false);

        // Act
        var result = await _service.UpdateAsync(appId, updateDto);

        // Assert
        result.Should().BeFalse();
        _repositoryMock.Verify(r => r.UpdateApplicationWithNetworkAsync(appId, updateDto), Times.Once);
    }
}

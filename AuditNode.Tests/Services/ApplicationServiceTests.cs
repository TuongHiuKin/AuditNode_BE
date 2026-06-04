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
    public async Task UpdateAsync_ShouldReturnTrue_WhenAppExists()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var existingApp = new AppEntity { Id = appId, AppCode = "TEST" };
        var updateDto = new UpdateApplicationDto { AppName = "New Name", OwnerTeam = "New Team" };

        _repositoryMock.Setup(r => r.GetByIdAsync(appId)).ReturnsAsync(existingApp);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<AppEntity>())).Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpdateAsync(appId, updateDto);

        // Assert
        result.Should().BeTrue();
        existingApp.AppName.Should().Be("New Name");
        existingApp.OwnerTeam.Should().Be("New Team");
        _repositoryMock.Verify(r => r.UpdateAsync(existingApp), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturnFalse_WhenAppDoesNotExist()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var updateDto = new UpdateApplicationDto { AppName = "New Name" };

        _repositoryMock.Setup(r => r.GetByIdAsync(appId)).ReturnsAsync((AppEntity?)null);

        // Act
        var result = await _service.UpdateAsync(appId, updateDto);

        // Assert
        result.Should().BeFalse();
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<AppEntity>()), Times.Never);
    }
}

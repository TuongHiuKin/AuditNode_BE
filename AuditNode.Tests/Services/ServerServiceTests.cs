using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Services;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public class ServerServiceTests
{
    private readonly Mock<IServerRepository> _repositoryMock;
    private readonly ServerService _service;

    public ServerServiceTests()
    {
        _repositoryMock = new Mock<IServerRepository>();
        _service = new ServerService(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnServerDetail_WhenServerExists()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var server = new Server
        {
            Id = serverId,
            Hostname = "SRV-TEST",
            IpAddress = "10.0.0.1",
            Datacenter = new Datacenter { Name = "DC-1" }
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(serverId)).ReturnsAsync(server);

        // Act
        var result = await _service.GetByIdAsync(serverId);

        // Assert
        result.Should().NotBeNull();
        result!.Hostname.Should().Be("SRV-TEST");
        result.IpAddress.Should().Be("10.0.0.1");
        result.DatacenterName.Should().Be("DC-1");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenServerDoesNotExist()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        _repositoryMock.Setup(r => r.GetByIdAsync(serverId)).ReturnsAsync((Server?)null);

        // Act
        var result = await _service.GetByIdAsync(serverId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturnTrue_WhenServerExists()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var existingServer = new Server { Id = serverId, Hostname = "Old" };
        var updateDto = new UpdateServerDto { Hostname = "New", DatacenterId = Guid.NewGuid() };

        _repositoryMock.Setup(r => r.GetByIdAsync(serverId)).ReturnsAsync(existingServer);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Server>())).Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpdateAsync(serverId, updateDto);

        // Assert
        result.Should().BeTrue();
        existingServer.Hostname.Should().Be("New");
        _repositoryMock.Verify(r => r.UpdateAsync(existingServer), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturnFalse_WhenServerDoesNotExist()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var updateDto = new UpdateServerDto { Hostname = "New" };

        _repositoryMock.Setup(r => r.GetByIdAsync(serverId)).ReturnsAsync((Server?)null);

        // Act
        var result = await _service.UpdateAsync(serverId, updateDto);

        // Assert
        result.Should().BeFalse();
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Server>()), Times.Never);
    }
}

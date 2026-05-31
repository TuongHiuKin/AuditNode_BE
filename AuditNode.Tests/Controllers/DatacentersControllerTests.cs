using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class DatacentersControllerTests
{
    private readonly Mock<IDatacenterRepository> _mockRepo;
    private readonly DatacentersController _controller;

    public DatacentersControllerTests()
    {
        _mockRepo = new Mock<IDatacenterRepository>();
        _controller = new DatacentersController(_mockRepo.Object);
    }

    [Fact]
    public async Task GetDatacenters_ReturnsOkResult_WithListOfDatacenterDtos()
    {
        // Arrange
        var datacenters = new List<Datacenter>
        {
            new Datacenter { Id = Guid.NewGuid(), Name = "DC1", Location = "Loc1" },
            new Datacenter { Id = Guid.NewGuid(), Name = "DC2", Location = "Loc2" }
        };
        _mockRepo.Setup(repo => repo.GetAllDatacentersAsync()).ReturnsAsync(datacenters);

        // Act
        var result = await _controller.GetDatacenters();

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        var returnedDtos = okResult.Value.As<IEnumerable<DatacenterDto>>();
        
        returnedDtos.Should().HaveCount(2);
        returnedDtos.Should().Contain(d => d.Name == "DC1");
        returnedDtos.Should().Contain(d => d.Name == "DC2");
    }

    [Fact]
    public async Task CreateDatacenter_ReturnsCreatedAtActionResult()
    {
        // Arrange
        var dto = new CreateDatacenterDto { Name = "New DC", Location = "New Loc" };
        var createdDatacenter = new Datacenter { Id = Guid.NewGuid(), Name = dto.Name, Location = dto.Location };
        
        _mockRepo.Setup(repo => repo.CreateDatacenterAsync(It.IsAny<Datacenter>()))
            .ReturnsAsync(createdDatacenter);

        // Act
        var result = await _controller.CreateDatacenter(dto);

        // Assert
        var createdAtActionResult = result.Result.As<CreatedAtActionResult>();
        createdAtActionResult.ActionName.Should().Be(nameof(DatacentersController.GetDatacenters));
        var returnedDatacenter = createdAtActionResult.Value.As<Datacenter>();
        returnedDatacenter.Name.Should().Be(dto.Name);
    }
}

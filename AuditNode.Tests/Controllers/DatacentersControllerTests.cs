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
    private readonly Mock<IDatacenterService> _mockService;
    private readonly DatacentersController _controller;

    public DatacentersControllerTests()
    {
        _mockService = new Mock<IDatacenterService>();
        _controller = new DatacentersController(_mockService.Object);
    }

    [Fact]
    public async Task GetDatacenters_ReturnsOkResult_WithListOfDatacenterDtos()
    {
        // Arrange
        var datacenters = new List<DatacenterDto>
        {
            new DatacenterDto { Id = Guid.NewGuid(), Name = "DC1" },
            new DatacenterDto { Id = Guid.NewGuid(), Name = "DC2" }
        };
        _mockService.Setup(s => s.GetDatacentersAsync()).ReturnsAsync(datacenters);

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
    public async Task CreateDatacenter_ReturnsOkResult()
    {
        // Arrange
        var dto = new CreateDatacenterDto { Name = "New DC", Location = "New Loc" };
        var createdDatacenter = new DatacenterDto { Id = Guid.NewGuid(), Name = dto.Name };
        
        _mockService.Setup(s => s.CreateDatacenterAsync(dto))
            .ReturnsAsync(createdDatacenter);

        // Act
        var result = await _controller.CreateDatacenter(dto);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        var returnedDatacenter = okResult.Value.As<DatacenterDto>();
        returnedDatacenter.Name.Should().Be(dto.Name);
    }
}

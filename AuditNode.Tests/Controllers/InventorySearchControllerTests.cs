using AuditNode.Application.Interfaces;
using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class InventorySearchControllerTests
{
    private readonly Mock<IInventorySearchService> _mockService;
    private readonly InventorySearchController _controller;

    public InventorySearchControllerTests()
    {
        _mockService = new Mock<IInventorySearchService>();
        _controller = new InventorySearchController(_mockService.Object);
    }

    [Fact]
    public async Task Search_ShouldReturnOk_WithResults()
    {
        // Arrange
        var keyword = "test";
        var mockResults = new List<SearchResultDto> { new SearchResultDto { Title = "Match 1" } };
        _mockService.Setup(s => s.SearchAsync(keyword)).ReturnsAsync(mockResults);

        // Act
        var result = await _controller.Search(keyword);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Value.Should().BeEquivalentTo(mockResults);
    }

    [Fact]
    public async Task Search_ShouldReturnEmpty_WhenKeywordIsNull()
    {
        // Arrange
        _mockService.Setup(s => s.SearchAsync(string.Empty)).ReturnsAsync(new List<SearchResultDto>());

        // Act
        var result = await _controller.Search(null);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Value.As<IEnumerable<SearchResultDto>>().Should().BeEmpty();
    }

    [Fact]
    public async Task Search_ShouldReturnBadRequest_OnException()
    {
        // Arrange
        _mockService.Setup(s => s.SearchAsync(It.IsAny<string>())).ThrowsAsync(new Exception("Search failed"));

        // Act
        var result = await _controller.Search("fail");

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}

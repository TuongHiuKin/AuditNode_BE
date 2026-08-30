using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
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
        _mockService.Setup(s => s.SearchAsync(keyword, It.IsAny<CatalogPageQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorPageDto<SearchResultDto>(mockResults, null, false));

        // Act
        var result = await _controller.Search(keyword);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Value.Should().BeEquivalentTo(new CursorPageDto<SearchResultDto>(mockResults, null, false));
    }

    [Fact]
    public async Task Search_ShouldReturnEmpty_WhenKeywordIsNull()
    {
        // Arrange
        _mockService.Setup(s => s.SearchAsync(string.Empty, It.IsAny<CatalogPageQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorPageDto<SearchResultDto>([], null, false));

        // Act
        var result = await _controller.Search(null);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Value.As<CursorPageDto<SearchResultDto>>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_ShouldReturnSafe500WithCorrelationId_OnException()
    {
        // Arrange
        _mockService.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CatalogPageQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Search failed"));

        // Act
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                TraceIdentifier = "corr-123"
            }
        };
        var result = await _controller.Search("fail");

        // Assert
        var failure = result.Result.Should().BeOfType<ObjectResult>().Subject;
        failure.StatusCode.Should().Be(500);
        var problem = failure.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["correlationId"].Should().Be("corr-123");
        problem.ToString().Should().NotContain("Search failed");
    }
}

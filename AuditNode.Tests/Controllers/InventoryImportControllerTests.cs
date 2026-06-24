using AuditNode.Application.Interfaces;
using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class InventoryImportControllerTests
{
    private readonly Mock<IInventoryImportService> _mockService;
    private readonly InventoryImportController _controller;

    public InventoryImportControllerTests()
    {
        _mockService = new Mock<IInventoryImportService>();
        _controller = new InventoryImportController(_mockService.Object);
    }

    [Fact]
    public void DownloadTemplate_ShouldReturnFile()
    {
        // Arrange
        var content = new byte[] { 1, 2, 3 };
        _mockService.Setup(s => s.GenerateTemplate()).Returns(content);

        // Act
        var result = _controller.DownloadTemplate();

        // Assert
        var fileResult = result.As<FileContentResult>();
        fileResult.FileContents.Should().BeEquivalentTo(content);
        fileResult.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    [Fact]
    public async Task ImportInventory_ShouldReturnOk_WhenFileIsValid()
    {
        // Arrange
        var fileMock = new Mock<IFormFile>();
        var content = "fake content";
        var fileName = "test.xlsx";
        var ms = new MemoryStream();
        var writer = new StreamWriter(ms);
        writer.Write(content);
        writer.Flush();
        ms.Position = 0;

        fileMock.Setup(_ => _.OpenReadStream()).Returns(ms);
        fileMock.Setup(_ => _.FileName).Returns(fileName);
        fileMock.Setup(_ => _.Length).Returns(ms.Length);

        var importResult = new ImportResponseDto { SavedCount = 10, Errors = new List<ImportErrorDto>() };
        _mockService.Setup(s => s.ImportInventoryAsync(It.IsAny<Stream>())).ReturnsAsync(importResult);

        // Act
        var result = await _controller.ImportInventory(fileMock.Object);

        // Assert
        var okResult = result.As<OkObjectResult>();
        okResult.Value.Should().BeEquivalentTo(importResult);
    }

    [Fact]
    public async Task ImportInventory_ShouldReturnBadRequest_WhenNoFile()
    {
        // Act
        var result = await _controller.ImportInventory(null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ImportInventory_ShouldReturnBadRequest_WhenInvalidExtension()
    {
        // Arrange
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(_ => _.FileName).Returns("test.txt");
        fileMock.Setup(_ => _.Length).Returns(10);

        // Act
        var result = await _controller.ImportInventory(fileMock.Object);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }
}

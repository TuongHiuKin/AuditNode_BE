using AuditNode.Infrastructure.Services;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using ClosedXML.Excel;
using Moq;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Services;

public class InventoryImportServiceTests
{
    private readonly AuditDbContext _context;
    private readonly InventoryImportService _service;

    public InventoryImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;        _context = new AuditDbContext(options);
        _service = new InventoryImportService(_context);
    }

    [Fact]
    public void GenerateTemplate_ShouldReturnValidExcelFile()
    {
        // Act
        var result = _service.GenerateTemplate();

        // Assert
        result.Should().NotBeNull();
        using var stream = new MemoryStream(result);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);
        
        worksheet.Cell(1, 1).Value.ToString().Should().Be("Server Name");
        worksheet.Cell(1, 4).Value.ToString().Should().Be("App Code");
    }

    [Fact]
    public async Task ImportInventoryAsync_ShouldProcessValidRows()
    {
        // Arrange
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Template");
        ws.Cell(1, 1).Value = "Server Name";
        ws.Cell(1, 2).Value = "IP";
        ws.Cell(1, 3).Value = "Environment";
        ws.Cell(1, 4).Value = "App Code";
        ws.Cell(1, 5).Value = "App Name";
        ws.Cell(1, 6).Value = "Owner Team";
        ws.Cell(1, 7).Value = "Port";
        ws.Cell(1, 8).Value = "Protocol";

        ws.Cell(2, 1).Value = "Server01";
        ws.Cell(2, 2).Value = "10.0.0.1";
        ws.Cell(2, 3).Value = "Production";
        ws.Cell(2, 4).Value = "APP01";
        ws.Cell(2, 5).Value = "App One";
        ws.Cell(2, 6).Value = "Team A";
        ws.Cell(2, 7).Value = 8080;
        ws.Cell(2, 8).Value = "HTTP";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        // Act
        var result = await _service.ImportInventoryAsync(stream, "test-owner");

        // Assert
        result.SavedCount.Should().Be(1);
        result.TotalProcessed.Should().Be(1);
        _context.Servers.Count().Should().Be(1);
        _context.Applications.Count().Should().Be(1);
        _context.PortMappings.Count().Should().Be(1);
    }

    [Fact]
    public async Task ImportInventoryAsync_ShouldReportConflicts_WhenAppCodeExistsWithDifferentName()
    {
        // Arrange
        var existingApp = new AppEntity 
        { 
            Id = Guid.NewGuid(), 
            AppCode = "APP01", 
            AppName = "Original Name", 
            OwnerTeam = "Original Team",
            OwnerId = "test-owner"
        };
        _context.Applications.Add(existingApp);
        await _context.SaveChangesAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Template");
        ws.Cell(1, 1).Value = "Server Name"; ws.Cell(1, 2).Value = "IP"; ws.Cell(1, 3).Value = "Environment"; 
        ws.Cell(1, 4).Value = "App Code"; ws.Cell(1, 5).Value = "App Name"; ws.Cell(1, 6).Value = "Owner Team";
        ws.Cell(1, 7).Value = "Port"; ws.Cell(1, 8).Value = "Protocol";

        ws.Cell(2, 1).Value = "Server01";
        ws.Cell(2, 2).Value = "10.0.0.1";
        ws.Cell(2, 3).Value = "Production";
        ws.Cell(2, 4).Value = "APP01";
        ws.Cell(2, 5).Value = "Different Name"; // Conflict
        ws.Cell(2, 6).Value = "Team A";
        ws.Cell(2, 7).Value = 8080;
        ws.Cell(2, 8).Value = "HTTP";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        // Act
        var result = await _service.ImportInventoryAsync(stream, "test-owner");

        // Assert
        result.Conflicts.Should().NotBeEmpty();
        result.Conflicts[0].AppCode.Should().Be("APP01");
        result.SavedCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportInventoryAsync_ShouldHandleMultipleRowsForSameNewServer()
    {
        // Arrange
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Template");
        ws.Cell(1, 1).Value = "Server Name"; ws.Cell(1, 2).Value = "IP"; ws.Cell(1, 3).Value = "Environment"; 
        ws.Cell(1, 4).Value = "App Code"; ws.Cell(1, 5).Value = "App Name"; ws.Cell(1, 6).Value = "Owner Team";
        ws.Cell(1, 7).Value = "Port"; ws.Cell(1, 8).Value = "Protocol";

        // Two rows for the same server "Server01" (IP 10.0.0.1)
        ws.Cell(2, 1).Value = "Server01"; ws.Cell(2, 2).Value = "10.0.0.1"; ws.Cell(2, 3).Value = "Production";
        ws.Cell(2, 4).Value = "APP01"; ws.Cell(2, 5).Value = "App One"; ws.Cell(2, 6).Value = "Team A";
        ws.Cell(2, 7).Value = 8080; ws.Cell(2, 8).Value = "HTTP";

        ws.Cell(3, 1).Value = "Server01"; ws.Cell(3, 2).Value = "10.0.0.1"; ws.Cell(3, 3).Value = "Production";
        ws.Cell(3, 4).Value = "APP02"; ws.Cell(3, 5).Value = "App Two"; ws.Cell(3, 6).Value = "Team A";
        ws.Cell(3, 7).Value = 9090; ws.Cell(3, 8).Value = "HTTPS";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        // Act
        var result = await _service.ImportInventoryAsync(stream, "test-owner");

        // Assert
        result.SavedCount.Should().Be(2);
        result.Errors.Should().BeEmpty();
        _context.Servers.Count().Should().Be(1); // Only 1 server should be created
        _context.Applications.Count().Should().Be(2);
        _context.PortMappings.Count().Should().Be(2);
    }
}

using AuditNode.Infrastructure.Services;
using AuditNode.Infrastructure.Data;
using AuditNode.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using ClosedXML.Excel;
using Moq;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuditNode.Tests.Services;

public class InventoryImportServiceTests
{
    private readonly AuditDbContext _context;
    private readonly InventoryImportService _service;
    private readonly Mock<IOwnerLabelService> _ownerLabels = new();

    public InventoryImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new AuditDbContext(options);
        _context.Datacenters.Add(new AuditNode.Domain.Entities.Datacenter
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", Name = "DC", Location = "Local"
        });
        _context.SaveChanges();
        _service = new InventoryImportService(_context, NullLogger<InventoryImportService>.Instance, User("owner"), _ownerLabels.Object);
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
        var result = await _service.ImportInventoryAsync(stream);

        // Assert
        result.SavedCount.Should().Be(1);
        result.TotalProcessed.Should().Be(1);
        _context.Servers.Count().Should().Be(1);
        _context.Applications.Count().Should().Be(1);
        _context.PortMappings.Count().Should().Be(1);
        _ownerLabels.Verify(item => item.EnsureAsync("owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportInventoryAsync_ShouldReportConflicts_WhenAppCodeExistsWithDifferentName()
    {
        // Arrange
        var existingApp = new AppEntity 
        { 
            Id = Guid.NewGuid(), 
            OwnerUserId = "owner",
            AppCode = "APP01", 
            AppName = "Original Name", 
            OwnerTeam = "Original Team"
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
        var result = await _service.ImportInventoryAsync(stream);

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
        var result = await _service.ImportInventoryAsync(stream);

        // Assert
        result.SavedCount.Should().Be(2);
        result.Errors.Should().BeEmpty();
        _context.Servers.Count().Should().Be(1); // Only 1 server should be created
        _context.Applications.Count().Should().Be(2);
        _context.PortMappings.Count().Should().Be(2);
    }

    [Fact]
    public async Task Corrupt_workbook_is_rejected_without_writes()
    {
        using var stream = new MemoryStream("not an xlsx"u8.ToArray());

        var result = await _service.ImportInventoryAsync(stream);

        result.Errors.Should().ContainSingle(error => error.Type == "Workbook");
        result.SavedCount.Should().Be(0);
        _context.Servers.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_headers_are_rejected_without_writes()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Bad");
        sheet.Cell(1, 1).Value = "Wrong";
        using var stream = Save(workbook);

        var result = await _service.ImportInventoryAsync(stream);

        result.Errors.Should().ContainSingle(error => error.Type == "Header");
        _context.Servers.Should().BeEmpty();
    }

    [Fact]
    public async Task Any_invalid_row_rolls_back_entire_import()
    {
        using var workbook = WorkbookWithHeaders();
        AddRow(workbook, 2, "srv-1", "10.0.0.1", "app1", 443);
        AddRow(workbook, 3, "srv-2", "999.0.0.1", "app2", 70000);
        using var stream = Save(workbook);

        var result = await _service.ImportInventoryAsync(stream);

        result.Errors.Should().NotBeEmpty();
        result.SavedCount.Should().Be(0);
        _context.Servers.Should().BeEmpty();
        _context.Applications.Should().BeEmpty();
        _context.PortMappings.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_rows_and_app_codes_are_case_insensitive()
    {
        using var workbook = WorkbookWithHeaders();
        AddRow(workbook, 2, "srv-1", "10.0.0.1", "app1", 443);
        AddRow(workbook, 3, "SRV-1", "10.0.0.1", "APP1", 443);
        using var stream = Save(workbook);

        var result = await _service.ImportInventoryAsync(stream);

        result.Conflicts.Should().ContainSingle();
        result.SavedCount.Should().Be(0);
        _context.PortMappings.Should().BeEmpty();
    }

    [Fact]
    public async Task App_code_is_normalized_before_persistence()
    {
        using var workbook = WorkbookWithHeaders();
        AddRow(workbook, 2, "srv-1", "10.0.0.1", "app1", 443);
        using var stream = Save(workbook);

        var result = await _service.ImportInventoryAsync(stream);

        result.SavedCount.Should().Be(1);
        _context.Applications.Should().ContainSingle(app => app.AppCode == "APP1");
    }

    [Fact]
    public async Task Persistence_failure_rolls_back_and_returns_generic_error()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var context = new FailingAuditDbContext(options);
        context.Datacenters.Add(new AuditNode.Domain.Entities.Datacenter
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", Name = "DC", Location = "Local"
        });
        await context.SaveChangesAsync();
        context.FailSaves = true;
        using var workbook = WorkbookWithHeaders();
        AddRow(workbook, 2, "srv-1", "10.0.0.1", "app1", 443);
        using var stream = Save(workbook);

        var result = await new InventoryImportService(context, NullLogger<InventoryImportService>.Instance, User("owner"), Mock.Of<IOwnerLabelService>())
            .ImportInventoryAsync(stream);

        result.Errors.Should().ContainSingle(error =>
            error.Type == "Transaction" && !error.Message.Contains("simulated database detail"));
        result.SavedCount.Should().Be(0);
        context.Servers.Should().BeEmpty();
        context.Applications.Should().BeEmpty();
        context.PortMappings.Should().BeEmpty();
    }

    private static XLWorkbook WorkbookWithHeaders()
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Template");
        var headers = new[] { "Server Name", "IP", "Environment", "App Code", "App Name", "Owner Team", "Port", "Protocol" };
        for (var index = 0; index < headers.Length; index++)
            sheet.Cell(1, index + 1).Value = headers[index];
        return workbook;
    }

    private static void AddRow(XLWorkbook workbook, int row, string server, string ip, string appCode, int port)
    {
        var sheet = workbook.Worksheet(1);
        sheet.Cell(row, 1).Value = server;
        sheet.Cell(row, 2).Value = ip;
        sheet.Cell(row, 3).Value = "Production";
        sheet.Cell(row, 4).Value = appCode;
        sheet.Cell(row, 5).Value = "App One";
        sheet.Cell(row, 6).Value = "Team A";
        sheet.Cell(row, 7).Value = port;
        sheet.Cell(row, 8).Value = "TCP";
    }

    private static MemoryStream Save(XLWorkbook workbook)
    {
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private sealed class FailingAuditDbContext : AuditDbContext
    {
        public bool FailSaves { get; set; }

        public FailingAuditDbContext(DbContextOptions<AuditDbContext> options)
            : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            FailSaves
                ? Task.FromException<int>(new InvalidOperationException("simulated database detail"))
                : base.SaveChangesAsync(cancellationToken);
    }

    private static ICurrentUserService User(string id)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(item => item.UserId).Returns(id);
        return user.Object;
    }
}

using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Services;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AuditNode.Database.IntegrationTests.Transactions;

[Trait("Category", "PostgreSQL")]
[Trait("Suite", "Relational")]
[Collection(PostgreSqlCollection.Name)]
public sealed class InventoryImportRollbackTests(PostgreSqlDatabaseFixture database)
{
    [Fact]
    public async Task Late_constraint_failure_rolls_back_every_import_side_effect()
    {
        var owner = $"inventory-owner-{Guid.NewGuid():N}";
        var datacenter = new Datacenter
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, Name = "Inventory rollback", Location = "Test"
        };
        await using (var seed = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString))
        {
            seed.Datacenters.Add(datacenter);
            await seed.SaveChangesAsync();
        }

        await using var failure = await PostgreSqlFailureTrigger.ForPortMappingAsync(
            database.ConnectionString, 65003);
        await using var context = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(owner);
        var service = new InventoryImportService(
            context,
            NullLogger<InventoryImportService>.Instance,
            currentUser.Object,
            new OwnerLabelService(context, TimeProvider.System));
        using var workbook = Workbook();
        using var stream = Save(workbook);

        var result = await service.ImportInventoryAsync(stream);

        result.TotalProcessed.Should().Be(3);
        result.SavedCount.Should().Be(0);
        result.Errors.Should().ContainSingle(error =>
            error.Type == "Transaction" &&
            !error.Message.Contains("synthetic PostgreSQL failure", StringComparison.OrdinalIgnoreCase));

        await using var verification = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        (await verification.Datacenters.CountAsync(item => item.OwnerUserId == owner)).Should().Be(1);
        (await verification.Servers.CountAsync(item => item.OwnerUserId == owner)).Should().Be(0);
        (await verification.Applications.CountAsync(item => item.OwnerUserId == owner)).Should().Be(0);
        (await verification.PortMappings.CountAsync(item => item.OwnerUserId == owner)).Should().Be(0);
        (await verification.Labels.CountAsync(item => item.OwnerUserId == owner)).Should().Be(0,
            "owner-label creation is part of the import operation and must share its transaction");
    }

    private static XLWorkbook Workbook()
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Template");
        var headers = new[]
        {
            "Server Name", "IP", "Environment", "App Code", "App Name", "Owner Team", "Port", "Protocol"
        };
        for (var index = 0; index < headers.Length; index++)
            sheet.Cell(1, index + 1).Value = headers[index];

        AddRow(sheet, 2, "rollback-server-1", "10.240.1.1", "ROLLBACK-APP-1", 65001);
        AddRow(sheet, 3, "rollback-server-2", "10.240.1.2", "ROLLBACK-APP-2", 65002);
        AddRow(sheet, 4, "rollback-server-3", "10.240.1.3", "ROLLBACK-APP-3", 65003);
        return workbook;
    }

    private static void AddRow(IXLWorksheet sheet, int row, string server, string ip, string app, int port)
    {
        sheet.Cell(row, 1).Value = server;
        sheet.Cell(row, 2).Value = ip;
        sheet.Cell(row, 3).Value = "Production";
        sheet.Cell(row, 4).Value = app;
        sheet.Cell(row, 5).Value = app;
        sheet.Cell(row, 6).Value = "Test Team";
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
}

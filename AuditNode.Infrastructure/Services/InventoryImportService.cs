using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Infrastructure.Services;

public class InventoryImportService : IInventoryImportService
{
    private static readonly string[] Headers =
    [
        "Server Name", "IP", "Environment", "App Code", "App Name", "Owner Team", "Port", "Protocol"
    ];

    private readonly AuditDbContext _context;
    private readonly ILogger<InventoryImportService> _logger;
    private readonly ICurrentUserService _currentUser;

    public InventoryImportService(AuditDbContext context, ILogger<InventoryImportService> logger, ICurrentUserService currentUser)
    {
        _context = context;
        _logger = logger;
        _currentUser = currentUser;
    }

    public byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Template");
        for (var index = 0; index < Headers.Length; index++)
        {
            var cell = worksheet.Cell(1, index + 1);
            cell.Value = Headers[index];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D3D3D3");
        }

        worksheet.Range("C2:C1000").CreateDataValidation().List("Production,Development", true);
        worksheet.Range("H2:H1000").CreateDataValidation().List("TCP,UDP,HTTP,HTTPS,gRPC", true);
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<ImportResponseDto> ImportInventoryAsync(Stream excelStream)
    {
        var response = new ImportResponseDto();
        var actor = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actor))
        {
            AddError(response, 0, "Authorization", "An authenticated catalog owner is required.");
            return response;
        }

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(excelStream);
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Inventory workbook was rejected as {ExceptionType}.", exception.GetType().Name);
            AddError(response, 0, "Workbook", "The uploaded file is not a valid .xlsx workbook.");
            return response;
        }

        using (workbook)
        {
            if (workbook.Worksheets.Count == 0)
            {
                AddError(response, 0, "Workbook", "The workbook must contain a worksheet.");
                return response;
            }

            var worksheet = workbook.Worksheet(1);
            for (var index = 0; index < Headers.Length; index++)
            {
                var actual = worksheet.Cell(1, index + 1).GetString().Trim();
                if (!actual.Equals(Headers[index], StringComparison.OrdinalIgnoreCase))
                {
                    AddError(response, 1, "Header", $"Column {index + 1} must be '{Headers[index]}'.");
                    return response;
                }
            }

            var rows = worksheet.RowsUsed().Where(row => row.RowNumber() > 1).ToArray();
            if (rows.Length == 0)
            {
                AddError(response, 0, "Workbook", "The workbook does not contain inventory rows.");
                return response;
            }

            var parsedRows = new List<ImportRow>();
            foreach (var row in rows)
            {
                response.TotalProcessed++;
                var parsed = ParseRow(row, response);
                if (parsed is not null)
                    parsedRows.Add(parsed);
            }

            DetectPayloadConflicts(parsedRows, response);
            if (response.Errors.Count > 0 || response.Conflicts.Count > 0)
                return response;

            var datacenter = await _context.Datacenters.AsNoTracking().Where(item => item.OwnerUserId == actor).OrderBy(item => item.Id).FirstOrDefaultAsync();
            if (datacenter is null)
            {
                AddError(response, 0, "Validation", "Your catalog does not have a datacenter.");
                return response;
            }

            var existingAppRows = await _context.Applications.AsNoTracking().Where(item => item.OwnerUserId == actor).ToListAsync();
            var duplicateExistingCodes = existingAppRows
                .GroupBy(app => app.AppCode, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateExistingCodes is not null)
            {
                response.Conflicts.Add(new ImportConflictDto
                {
                    Row = 0,
                    AppCode = duplicateExistingCodes.Key,
                    Message = "Your catalog contains application codes that differ only by case."
                });
                return response;
            }
            var existingApps = existingAppRows.ToDictionary(app => app.AppCode, StringComparer.OrdinalIgnoreCase);
            var existingServers = (await _context.Servers.AsNoTracking().Where(item => item.OwnerUserId == actor).ToListAsync())
                .ToDictionary(server => server.IpAddress, StringComparer.OrdinalIgnoreCase);

            foreach (var row in parsedRows)
            {
                if (existingApps.TryGetValue(row.AppCode, out var app) &&
                    (!app.AppName.Equals(row.AppName, StringComparison.OrdinalIgnoreCase) ||
                     !app.OwnerTeam.Equals(row.OwnerTeam, StringComparison.OrdinalIgnoreCase)))
                    AddConflict(response, row, "Application code already exists with different metadata.");

                if (existingServers.TryGetValue(row.IpAddress, out var server) &&
                    (!server.Hostname.Equals(row.ServerName, StringComparison.OrdinalIgnoreCase) ||
                     !server.Environment.Equals(row.Environment, StringComparison.OrdinalIgnoreCase)))
                    AddConflict(response, row, "Server IP already exists with different metadata.");
            }

            var existingServerIds = existingServers.Values.Select(server => server.Id).ToArray();
            var existingMappings = await _context.PortMappings.AsNoTracking()
                .Where(mapping => existingServerIds.Contains(mapping.ServerId))
                .ToListAsync();
            var mappingLookup = existingMappings.ToDictionary(
                mapping => $"{mapping.ServerId:N}:{mapping.PortNumber}",
                StringComparer.OrdinalIgnoreCase);
            foreach (var row in parsedRows.Where(row => existingServers.ContainsKey(row.IpAddress)))
            {
                var server = existingServers[row.IpAddress];
                if (!mappingLookup.TryGetValue($"{server.Id:N}:{row.Port}", out var mapping))
                    continue;
                if (!existingApps.TryGetValue(row.AppCode, out var app) || mapping.AppId != app.Id ||
                    !mapping.Protocol.Equals(row.Protocol, StringComparison.OrdinalIgnoreCase))
                    AddConflict(response, row, "The server port is already assigned to another deployment.");
            }

            if (response.Conflicts.Count > 0)
                return response;

            await PersistAsync(parsedRows, datacenter.Id, actor, existingApps, existingServers, mappingLookup, response);
            return response;
        }
    }

    private async Task PersistAsync(
        IReadOnlyCollection<ImportRow> rows,
        Guid datacenterId,
        string ownerUserId,
        IDictionary<string, AppEntity> apps,
        IDictionary<string, Server> servers,
        IDictionary<string, PortMapping> mappings,
        ImportResponseDto response)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            foreach (var row in rows)
            {
                if (!servers.TryGetValue(row.IpAddress, out var server))
                {
                    server = new Server
                    {
                        Id = Guid.NewGuid(), OwnerUserId = ownerUserId, DatacenterId = datacenterId, Hostname = row.ServerName,
                        IpAddress = row.IpAddress, Environment = row.Environment, OsType = "Unknown", Status = "Active"
                    };
                    servers.Add(row.IpAddress, server);
                    _context.Servers.Add(server);
                }

                if (!apps.TryGetValue(row.AppCode, out var application))
                {
                    application = new AppEntity
                    {
                        Id = Guid.NewGuid(), OwnerUserId = ownerUserId, AppCode = row.AppCode, AppName = row.AppName,
                        OwnerTeam = row.OwnerTeam, Risk = "MEDIUM"
                    };
                    apps.Add(row.AppCode, application);
                    _context.Applications.Add(application);
                }

                var key = $"{server.Id:N}:{row.Port}";
                if (!mappings.ContainsKey(key))
                {
                    var mapping = new PortMapping
                    {
                        Id = Guid.NewGuid(), OwnerUserId = ownerUserId, ServerId = server.Id, AppId = application.Id,
                        PortNumber = row.Port, Protocol = row.Protocol
                    };
                    mappings.Add(key, mapping);
                    _context.PortMappings.Add(mapping);
                }
                response.SavedCount++;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            response.SavedCount = 0;
            AddError(response, 0, "Transaction", "The inventory import could not be saved.");
            _logger.LogError("Inventory persistence failed with {ExceptionType}.", exception.GetType().Name);
        }
    }

    private static ImportRow? ParseRow(IXLRow row, ImportResponseDto response)
    {
        var rowNumber = row.RowNumber();
        var serverName = row.Cell(1).GetString().Trim();
        var ipAddress = row.Cell(2).GetString().Trim();
        var environment = row.Cell(3).GetString().Trim();
        var appCode = row.Cell(4).GetString().Trim().ToUpperInvariant();
        var appName = row.Cell(5).GetString().Trim();
        var ownerTeam = row.Cell(6).GetString().Trim();
        var portText = row.Cell(7).GetString().Trim();
        var protocol = row.Cell(8).GetString().Trim().ToUpperInvariant();

        if (new[] { serverName, ipAddress, environment, appCode, appName, ownerTeam, protocol }.Any(string.IsNullOrWhiteSpace))
        {
            AddError(response, rowNumber, "Validation", "All inventory fields are required.");
            return null;
        }
        if (!IsIpv4(ipAddress))
        {
            AddError(response, rowNumber, "Validation", "IP must be a valid IPv4 address.");
            return null;
        }
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            AddError(response, rowNumber, "Validation", "Port must be between 1 and 65535.");
            return null;
        }

        return new ImportRow(rowNumber, serverName, ipAddress, environment, appCode, appName, ownerTeam, port, protocol);
    }

    private static void DetectPayloadConflicts(IReadOnlyCollection<ImportRow> rows, ImportResponseDto response)
    {
        foreach (var duplicate in rows.GroupBy(
                     row => $"{row.IpAddress}|{row.AppCode}|{row.Port}|{row.Protocol}",
                     StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            AddConflict(response, duplicate.Skip(1).First(), "Duplicate inventory row.");

        foreach (var collision in rows.GroupBy(
                     row => $"{row.IpAddress}|{row.Port}",
                     StringComparer.OrdinalIgnoreCase).Where(group =>
                         group.Select(row => $"{row.AppCode}|{row.Protocol}").Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            AddConflict(response, collision.Skip(1).First(), "Multiple deployments use the same server port.");

        foreach (var appGroup in rows.GroupBy(row => row.AppCode, StringComparer.OrdinalIgnoreCase).Where(group =>
                     group.Select(row => $"{row.AppName}|{row.OwnerTeam}").Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            AddConflict(response, appGroup.Skip(1).First(), "Application code has conflicting metadata in the workbook.");
    }

    private static bool IsIpv4(string value)
    {
        var octets = value.Split('.');
        return octets.Length == 4 && octets.All(octet =>
            octet.Length > 0 && (octet.Length == 1 || octet[0] != '0') &&
            octet.All(char.IsAsciiDigit) && byte.TryParse(octet, out _));
    }

    private static void AddError(ImportResponseDto response, int row, string type, string message) =>
        response.Errors.Add(new ImportErrorDto { Row = row, Type = type, Message = message });

    private static void AddConflict(ImportResponseDto response, ImportRow row, string message) =>
        response.Conflicts.Add(new ImportConflictDto { Row = row.RowNumber, AppCode = row.AppCode, Message = message });

    private sealed record ImportRow(
        int RowNumber,
        string ServerName,
        string IpAddress,
        string Environment,
        string AppCode,
        string AppName,
        string OwnerTeam,
        int Port,
        string Protocol);
}

using System.Data;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public class InventoryImportService : IInventoryImportService
{
    private readonly AuditDbContext _context;

    public InventoryImportService(AuditDbContext context)
    {
        _context = context;
    }

    public byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Template");

        // Define Headers in Row 1
        var headers = new[] { "Server Name", "IP", "Environment", "App Code", "App Name", "Owner Team", "Port", "Protocol" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D3D3D3"); // Light Gray
        }

        // Add Excel Data Validation (Dropdowns) for Column C (Environment)
        var environmentRange = worksheet.Range("C2:C1000");
        environmentRange.SetDataValidation().List("Production,Development", true);

        // Add Excel Data Validation (Dropdowns) for Column H (Protocol)
        var protocolRange = worksheet.Range("H2:H1000");
        protocolRange.SetDataValidation().List("TCP,UDP,HTTP,HTTPS,gRPC", true);

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<ImportResponseDto> ImportInventoryAsync(Stream excelStream)
    {
        var response = new ImportResponseDto();
        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheet(1);
        var rows = worksheet.RowsUsed().Skip(1).ToList(); // Skip header row and materialize to avoid multiple passes

        // OPTIMIZATION: Extract all AppCodes and IPs upfront to eliminate N+1 queries
        var allAppCodes = rows.Select(r => r.Cell(4).GetValue<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        var allIps = rows.Select(r => r.Cell(2).GetValue<string>()).Where(ip => !string.IsNullOrWhiteSpace(ip)).Distinct().ToList();

        // Pre-fetch existing Apps and Servers for fast memory lookup
        var existingAppsDict = await _context.Applications
            .AsNoTracking()
            .Where(a => allAppCodes.Contains(a.AppCode))
            .ToDictionaryAsync(a => a.AppCode);

        var existingServersDict = await _context.Servers
            .AsNoTracking()
            .Where(s => allIps.Contains(s.IpAddress))
            .ToDictionaryAsync(s => s.IpAddress);

        var validRows = new List<(int RowNum, string ServerName, string Ip, string Env, string AppCode, string AppName, string OwnerTeam, int Port, string Protocol)>();

        foreach (var row in rows)
        {
            response.TotalProcessed++;
            int rowNumber = row.RowNumber();

            var serverName = row.Cell(1).GetValue<string>();
            var ip = row.Cell(2).GetValue<string>();
            var env = row.Cell(3).GetValue<string>();
            var appCode = row.Cell(4).GetValue<string>();
            var appName = row.Cell(5).GetValue<string>();
            var ownerTeam = row.Cell(6).GetValue<string>();
            var portRaw = row.Cell(7).GetValue<string>();
            var protocol = row.Cell(8).GetValue<string>();

            // 1. Pre-validation: Essential fields
            if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(appCode) || string.IsNullOrWhiteSpace(ip))
            {
                response.Errors.Add(new ImportErrorDto { Row = rowNumber, Type = "Validation", Message = "Server Name, IP, and App Code are required." });
                continue;
            }

            if (!int.TryParse(portRaw, out int port))
            {
                response.Errors.Add(new ImportErrorDto { Row = rowNumber, Type = "Validation", Message = $"Invalid Port number: {portRaw}" });
                continue;
            }

            // 2. Conflict Logic - Using In-Memory Dictionary (FIXED N+1)
            if (existingAppsDict.TryGetValue(appCode, out var existingApp))
            {
                if (existingApp.AppName != appName || existingApp.OwnerTeam != ownerTeam)
                {
                    response.Conflicts.Add(new ImportConflictDto
                    {
                        Row = rowNumber,
                        AppCode = appCode,
                        Message = $"AppCode {appCode} already exists with a different name ({existingApp.AppName}) or owner team ({existingApp.OwnerTeam})."
                    });
                    continue; // SKIP the row
                }
            }

            validRows.Add((rowNumber, serverName, ip, env, appCode, appName, ownerTeam, port, protocol));
        }

        if (validRows.Count == 0) return response;

        // 3. Upsert Transaction
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Ensure at least one datacenter exists - FIXED WARNING 10103
            var datacenter = await _context.Datacenters
                .OrderBy(d => d.Id)
                .FirstOrDefaultAsync();

            if (datacenter == null)
            {
                datacenter = new Datacenter { Id = Guid.NewGuid(), Name = "Default DC", Location = "Auto-generated" };
                _context.Datacenters.Add(datacenter);
                await _context.SaveChangesAsync();
            }

            // Tracking dictionaries for the current transaction (including newly created ones)
            var currentServers = existingServersDict.ToDictionary(k => k.Key, v => v.Value);
            var currentApps = existingAppsDict.ToDictionary(k => k.Key, v => v.Value);

            // Re-attach existing tracked entities if necessary or use their IDs
            // Note: Since we used AsNoTracking() for pre-fetch, we need to handle upsert carefully.

            // STEP 1: Group by Server (Distinct IP) from validRows
            var distinctServersToProcess = validRows
                .GroupBy(v => v.Ip)
                .Select(g => g.First())
                .ToList();

            foreach (var ds in distinctServersToProcess)
            {
                if (!currentServers.TryGetValue(ds.Ip, out var server))
                {
                    server = new Server
                    {
                        Id = Guid.NewGuid(),
                        Hostname = ds.ServerName,
                        IpAddress = ds.Ip,
                        Environment = ds.Env,
                        DatacenterId = datacenter.Id,
                        OsType = "Unknown",
                        Status = "Active"
                    };
                    _context.Servers.Add(server);
                    currentServers.Add(server.IpAddress, server);
                }
            }
            await _context.SaveChangesAsync(); // Persist servers first

            // STEP 2: Handle Applications (Unique AppCode)
            var distinctAppsToProcess = validRows
                .GroupBy(v => v.AppCode)
                .Select(g => g.First())
                .ToList();

            foreach (var da in distinctAppsToProcess)
            {
                if (!currentApps.TryGetValue(da.AppCode, out var application))
                {
                    var server = currentServers[da.Ip];
                    application = new Domain.Entities.Application
                    {
                        Id = Guid.NewGuid(),
                        AppCode = da.AppCode,
                        AppName = da.AppName,
                        OwnerTeam = da.OwnerTeam,
                        ServerId = server.Id,
                        Risk = "Medium"
                    };
                    _context.Applications.Add(application);
                    currentApps.Add(application.AppCode, application);
                }
                else
                {
                    // If App exists, ensure it's attached to the Context before modifying
                    _context.Attach(application); 
                    application.ServerId = currentServers[da.Ip].Id;
                }
            }
            await _context.SaveChangesAsync(); // Persist applications

            // STEP 3: Pre-fetch Existing Port Mappings for involved servers to avoid collisions
            var serverIds = currentServers.Values.Select(s => s.Id).ToList();
            var existingMappings = await _context.PortMappings
                .AsNoTracking()
                .Where(pm => serverIds.Contains(pm.ServerId))
                .ToListAsync();

            // Key format: "ServerId:PortNumber"
            var mappingLookup = existingMappings
                .GroupBy(pm => $"{pm.ServerId}:{pm.PortNumber}")
                .ToDictionary(g => g.Key, g => g.First());

            // STEP 4: Create Port Mappings for all rows
            foreach (var v in validRows)
            {
                var server = currentServers[v.Ip];
                var application = currentApps[v.AppCode];
                var mappingKey = $"{server.Id}:{v.Port}";

                if (mappingLookup.TryGetValue(mappingKey, out var existingPm))
                {
                    // IF EXACT MATCH: Same App and Protocol -> SKIP (Idempotency)
                    if (existingPm.AppId == application.Id && existingPm.Protocol == v.Protocol)
                    {
                        response.SavedCount++; // Still count as successful since it's correctly in DB
                        continue;
                    }

                    // IF COLLISION: Port in use by another app or different protocol
                    response.Conflicts.Add(new ImportConflictDto
                    {
                        Row = v.RowNum,
                        AppCode = v.AppCode,
                        Message = $"Port {v.Port} on server {server.Hostname} ({server.IpAddress}) is already in use by another application mapping."
                    });
                    continue;
                }

                var portMapping = new PortMapping
                {
                    Id = Guid.NewGuid(),
                    ServerId = server.Id,
                    AppId = application.Id,
                    PortNumber = v.Port,
                    Protocol = v.Protocol
                };
                _context.PortMappings.Add(portMapping);
                
                // Add to lookup to prevent collisions within the same file import
                mappingLookup.Add(mappingKey, portMapping);
                response.SavedCount++;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            response.Errors.Add(new ImportErrorDto { Row = 0, Type = "Transaction", Message = $"Internal error during save: {ex.Message}" });
            response.SavedCount = 0;
        }

        return response;
    }
}

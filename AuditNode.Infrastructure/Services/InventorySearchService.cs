using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public class InventorySearchService : IInventorySearchService
{
    private readonly AuditDbContext _context;

    public InventorySearchService(AuditDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<SearchResultDto>> SearchAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2)
        {
            return Enumerable.Empty<SearchResultDto>();
        }

        var lowerKeyword = keyword.ToLower();

        // 1. Server Query - Direct projection ensures only needed columns are fetched
        var serverQuery = _context.Servers
            .Where(s => s.Hostname.ToLower().Contains(lowerKeyword) || s.IpAddress.ToLower().Contains(lowerKeyword))
            .Select(s => new SearchResultDto
            {
                Id = s.Id,
                Type = "SERVER",
                Title = s.Hostname,
                Subtitle = $"IP: {s.IpAddress}",
                MatchReason = s.Hostname.ToLower().Contains(lowerKeyword) ? "Matched by Server Hostname" : "Matched by Server IP"
            })
            .Take(20);

        // 2. Application Query - Join with hosting context projected directly to DTO to avoid record duplication
        var appQuery = _context.Applications
            .Where(a => a.AppName.ToLower().Contains(lowerKeyword) || a.AppCode.ToLower().Contains(lowerKeyword))
            .Select(a => new SearchResultDto
            {
                Id = a.Id,
                Type = "APP",
                Title = a.AppName,
                Subtitle = $"On Server: {(a.PortMappings.OrderBy(pm => pm.PortNumber).Select(pm => pm.Server.Hostname).FirstOrDefault() ?? "Unknown")} (Port: {(a.PortMappings.OrderBy(p => p.PortNumber).Select(p => p.PortNumber.ToString()).FirstOrDefault() ?? "N/A")})",
                MatchReason = a.AppName.ToLower().Contains(lowerKeyword) ? "Matched by App Name" : "Matched by App Code"
            })
            .Take(20);

        // Execute and combine results. Limited to Top 20 overall.
        var servers = await serverQuery.ToListAsync();
        var apps = await appQuery.ToListAsync();

        return servers.Concat(apps)
            .Take(20)
            .ToList();
    }
}

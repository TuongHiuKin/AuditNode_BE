using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public class InventorySearchService : IInventorySearchService
{
    private readonly AuditDbContext _context;
    private readonly IScopedResourcePolicy _policy;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantProvider _tenant;
    private readonly IGlobalCatalogRepository _catalog;
    private readonly TimeProvider _timeProvider;

    public InventorySearchService(AuditDbContext context, IScopedResourcePolicy policy, ICurrentUserService currentUser, ITenantProvider tenant, IGlobalCatalogRepository catalog, TimeProvider timeProvider)
    {
        _context = context;
        _policy = policy;
        _currentUser = currentUser;
        _tenant = tenant;
        _catalog = catalog;
        _timeProvider = timeProvider;
    }

    public Task<CursorPageDto<SearchResultDto>> SearchAsync(string keyword, CatalogPageQuery query, string? ownerUserId = null, string? labelKey = null, string? labelValue = null, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult(new CursorPageDto<SearchResultDto>([], null, false))
            : _catalog.SearchAsync(_currentUser.UserId!, keyword, query, _timeProvider.GetUtcNow().UtcDateTime, ownerUserId, labelKey, labelValue, cancellationToken);

    public async Task<IEnumerable<SearchResultDto>> SearchAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2)
        {
            return Enumerable.Empty<SearchResultDto>();
        }

        var lowerKeyword = keyword.ToLower();
        if (!_tenant.WorkspaceId.HasValue || string.IsNullOrWhiteSpace(_currentUser.UserId)) return [];
        var allowedServers = await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "server");
        var allowedApplications = await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "application");

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
        if (allowedServers is not null) serverQuery = serverQuery.Where(result => allowedServers.Contains(result.Id));

        // 2. Application Query - Join with hosting context projected directly to DTO to avoid record duplication
        var appQuery = _context.Applications
            .Where(a => a.AppName.ToLower().Contains(lowerKeyword) || a.AppCode.ToLower().Contains(lowerKeyword))
            .Select(a => new SearchResultDto
            {
                Id = a.Id,
                Type = "APP",
                Title = a.AppName,
                Subtitle = $"On Server: {(a.PortMappings.OrderBy(pm => pm.PortNumber).Select(pm => pm.Server != null ? pm.Server.Hostname : null).FirstOrDefault() ?? "Unknown")} (Port: {(a.PortMappings.OrderBy(p => p.PortNumber).Select(p => p.PortNumber.ToString()).FirstOrDefault() ?? "N/A")})",
                MatchReason = a.AppName.ToLower().Contains(lowerKeyword) ? "Matched by App Name" : "Matched by App Code"
            })
            .Take(20);
        if (allowedApplications is not null) appQuery = appQuery.Where(result => allowedApplications.Contains(result.Id));

        // Execute and combine results. Limited to Top 20 overall.
        var servers = await serverQuery.ToListAsync();
        var apps = await appQuery.ToListAsync();

        return servers.Concat(apps)
            .Take(20)
            .ToList();
    }
}

using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Repositories;

public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly AuditDbContext _dbContext;
    private readonly IScopedResourcePolicy _policy;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantProvider _tenant;

    public AnalyticsRepository(AuditDbContext dbContext, IScopedResourcePolicy policy, ICurrentUserService currentUser, ITenantProvider tenant)
    {
        _dbContext = dbContext;
        _policy = policy;
        _currentUser = currentUser;
        _tenant = tenant;
    }

    public async Task<IEnumerable<TopologyView>> GetTopologyAsync(string? environment = null, Guid? datacenterId = null)
    {
        var query = _dbContext.TopologyViews.AsQueryable();
        if (!_tenant.WorkspaceId.HasValue || string.IsNullOrWhiteSpace(_currentUser.UserId)) return [];
        var serverIds = await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "server");
        var applicationIds = await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "application");
        if (serverIds is not null) query = query.Where(x => serverIds.Contains(x.ServerId));
        if (applicationIds is not null) query = query.Where(x => applicationIds.Contains(x.AppId));

        if (!string.IsNullOrEmpty(environment))
        {
            query = query.Where(v => v.Environment == environment);
        }

        if (datacenterId.HasValue && datacenterId != Guid.Empty)
        {
            query = query.Where(v => v.DatacenterId == datacenterId.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<IEnumerable<DependencyView>> GetDependenciesAsync(string? environment = null, Guid? datacenterId = null)
    {
        var query = _dbContext.DependencyViews.AsQueryable();
        if (!_tenant.WorkspaceId.HasValue || string.IsNullOrWhiteSpace(_currentUser.UserId)) return [];
        var applicationIds = await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "application");
        if (applicationIds is not null) query = query.Where(x => applicationIds.Contains(x.SourceAppId) && applicationIds.Contains(x.DestAppId));

        if (!string.IsNullOrEmpty(environment))
        {
            query = query.Where(v => v.Environment == environment);
        }

        if (datacenterId.HasValue && datacenterId != Guid.Empty)
        {
            query = query.Where(v => v.DatacenterId == datacenterId.Value);
        }

        return await query.ToListAsync();
    }
}

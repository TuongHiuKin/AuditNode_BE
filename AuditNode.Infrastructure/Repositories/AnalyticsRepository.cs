using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Repositories;

public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly AuditDbContext _dbContext;

    public AnalyticsRepository(AuditDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<TopologyView>> GetTopologyAsync(string? environment = null, Guid? datacenterId = null)
    {
        var query = _dbContext.TopologyViews.AsQueryable();

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

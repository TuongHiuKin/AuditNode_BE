using AuditNode.Domain.Entities;

namespace AuditNode.Application.Interfaces;

public interface IAnalyticsRepository
{
    Task<IEnumerable<TopologyView>> GetTopologyAsync(string? environment = null, Guid? datacenterId = null);
    Task<IEnumerable<DependencyView>> GetDependenciesAsync(string? environment = null, Guid? datacenterId = null);
}

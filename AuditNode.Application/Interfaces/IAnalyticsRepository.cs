using AuditNode.Domain.Entities;
using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IAnalyticsRepository
{
    Task<IEnumerable<TopologyView>> GetTopologyAsync(string? environment = null, Guid? datacenterId = null);
    Task<IEnumerable<DependencyView>> GetDependenciesAsync(string? environment = null, Guid? datacenterId = null);
    Task<IEnumerable<TopologyView>> GetTopologyCatalogAsync(CatalogView view, string? environment = null, Guid? datacenterId = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<DependencyView>> GetDependenciesCatalogAsync(CatalogView view, string? environment = null, Guid? datacenterId = null, CancellationToken cancellationToken = default);
}

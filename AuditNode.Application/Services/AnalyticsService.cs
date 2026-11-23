using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;

namespace AuditNode.Application.Services;

/// <summary>
/// Analytics use cases. Keeps the API layer off the repository contract and
/// leaves room for report shaping without touching controllers.
/// </summary>
public sealed class AnalyticsService(IAnalyticsRepository repository) : IAnalyticsService
{
    public Task<IEnumerable<TopologyView>> GetTopologyAsync(
        CatalogView view,
        string? environment = null,
        Guid? datacenterId = null,
        CancellationToken cancellationToken = default) =>
        repository.GetTopologyCatalogAsync(view, environment, datacenterId, cancellationToken);

    public Task<IEnumerable<DependencyView>> GetDependenciesAsync(
        CatalogView view,
        string? environment = null,
        Guid? datacenterId = null,
        CancellationToken cancellationToken = default) =>
        repository.GetDependenciesCatalogAsync(view, environment, datacenterId, cancellationToken);
}

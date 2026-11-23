using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

/// <summary>
/// Read-only analytics use cases exposed to the API surface.
/// Controllers depend on this contract, never on the repository.
/// </summary>
public interface IAnalyticsService
{
    Task<IEnumerable<Domain.Entities.TopologyView>> GetTopologyAsync(
        CatalogView view,
        string? environment = null,
        Guid? datacenterId = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Domain.Entities.DependencyView>> GetDependenciesAsync(
        CatalogView view,
        string? environment = null,
        Guid? datacenterId = null,
        CancellationToken cancellationToken = default);
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;

namespace AuditNode.Application.Services;

/// <summary>
/// Topology use cases. Keeps the API layer off ITopologyRepository so the
/// persistence contract can change without touching controllers.
/// </summary>
public sealed class TopologyQueryService(ITopologyRepository repository) : ITopologyQueryService
{
    public Task<IEnumerable<TopologyTreeDto>> GetTreeAsync(
        Guid? datacenterId = null,
        int skip = 0,
        int take = 100,
        List<string>? labels = null,
        string? ownerUserId = null) =>
        repository.GetTopologyTreeAsync(datacenterId, skip, take, labels, ownerUserId);

    public Task<DependencyMapDto> GetDependencyMapAsync(
        string? environment = null,
        Guid? datacenterId = null,
        List<string>? labels = null,
        string? ownerUserId = null) =>
        repository.GetDependencyMapAsync(environment, datacenterId, labels, ownerUserId);

    public Task<IEnumerable<ApplicationStatusDto>> GetStatusAsync(string? ownerUserId = null) =>
        repository.GetApplicationStatusAsync(ownerUserId);

    public Task<TopologyStateDto> GetStateAsync(string? ownerUserId = null) =>
        repository.GetTopologyStateAsync(ownerUserId);

    public Task<TopologyStateStatus> SaveStateAsync(SaveTopologyStateDto state) =>
        repository.SaveTopologyStateAsync(state);
}

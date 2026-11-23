using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

/// <summary>
/// Topology read and persistence use cases. Controllers depend on this contract
/// so the repository stays an infrastructure detail.
/// </summary>
public interface ITopologyQueryService
{
    Task<IEnumerable<TopologyTreeDto>> GetTreeAsync(
        Guid? datacenterId = null,
        int skip = 0,
        int take = 100,
        List<string>? labels = null,
        string? ownerUserId = null);

    Task<DependencyMapDto> GetDependencyMapAsync(
        string? environment = null,
        Guid? datacenterId = null,
        List<string>? labels = null,
        string? ownerUserId = null);

    Task<IEnumerable<ApplicationStatusDto>> GetStatusAsync(string? ownerUserId = null);

    Task<TopologyStateDto> GetStateAsync(string? ownerUserId = null);

    Task<TopologyStateStatus> SaveStateAsync(SaveTopologyStateDto state);
}

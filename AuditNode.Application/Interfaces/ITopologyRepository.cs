using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ITopologyRepository
{
    Task<IEnumerable<TopologyTreeDto>> GetTopologyTreeAsync(Guid? datacenterId, int skip, int take);
    Task<DependencyMapDto> GetDependencyMapAsync(string[]? labels = null, string? environment = null, Guid? datacenterId = null);
    Task<IEnumerable<ApplicationStatusDto>> GetApplicationStatusAsync();
    Task<IEnumerable<ServerNodeDto>> GetExternalDependenciesAsync(Guid id, string[]? labels = null);
    Task SaveTopologyStateAsync(SaveTopologyStateDto state);
    Task SyncTopologyAsync(TopologySyncRequestDto request, string ownerId);
}

using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ITopologyRepository
{
    Task<IEnumerable<TopologyTreeDto>> GetTopologyTreeAsync(Guid? datacenterId = null, int skip = 0, int take = 100, List<string>? labels = null);
    Task<DependencyMapDto> GetDependencyMapAsync(string? environment = null, Guid? datacenterId = null, List<string>? labels = null);
    Task<IEnumerable<ApplicationStatusDto>> GetApplicationStatusAsync();
    Task<TopologyStateDto> GetTopologyStateAsync();
    Task<TopologyStateStatus> SaveTopologyStateAsync(SaveTopologyStateDto state);
}

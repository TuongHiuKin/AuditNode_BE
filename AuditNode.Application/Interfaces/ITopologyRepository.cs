using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ITopologyRepository
{
    Task<IEnumerable<TopologyTreeDto>> GetTopologyTreeAsync(Guid? datacenterId, int skip, int take);
    Task<DependencyMapDto> GetDependencyMapAsync(string? environment = null, Guid? datacenterId = null);
}

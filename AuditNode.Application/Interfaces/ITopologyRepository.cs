using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ITopologyRepository
{
    Task<IEnumerable<ServerTopologyDto>> GetTopologyTreeAsync(Guid? datacenterId, int skip, int take);
    Task<DependencyMapDto> GetDependencyMapAsync();
}

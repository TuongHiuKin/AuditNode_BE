using AuditNode.Domain.Entities;
using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IServerRepository
{
    Task<IEnumerable<ServerResponseDto>> GetAllWithAppsAsync(string? environment = null, Guid? datacenterId = null);
    Task<IEnumerable<ServerResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task<IEnumerable<ServerResponseDto>> GetScopedAsync(IReadOnlySet<Guid>? serverIds, IReadOnlySet<Guid>? applicationIds, IEnumerable<Guid>? requestedIds = null);
    Task<Server?> GetByIdAsync(Guid id);
    Task<bool> DatacenterExistsAsync(Guid id, string ownerUserId);
    Task<bool> IpAddressExistsAsync(string ipAddress, string ownerUserId, Guid? excludeServerId = null);
    Task<Server> CreateServerAsync(Server server, IReadOnlyCollection<LabelDto> labels);
    Task UpdateAsync(Server server, IReadOnlyCollection<LabelDto>? labels);
    Task DeleteAsync(Server server);
}

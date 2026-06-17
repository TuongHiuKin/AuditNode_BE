using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IServerService
{
    Task<IEnumerable<ServerResponseDto>> GetServersAsync();
    Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids);
}

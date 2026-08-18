using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IServerService
{
    Task<IEnumerable<ServerResponseDto>> GetServersAsync();
    Task<ServerResponseDto?> GetServerAsync(Guid id);
    Task<ServerOperationResult> CreateServerAsync(CreateServerDto dto);
    Task<ServerOperationResult> UpdateServerAsync(Guid id, UpdateServerDto dto);
    Task<ServerOperationStatus> PurgeServerAsync(Guid id);
    Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids);
}

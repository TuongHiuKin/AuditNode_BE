using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IServerService
{
    Task<IEnumerable<ServerResponseDto>> GetServersAsync();
    Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids);
    Task<ServerResponseDto?> GetServerByIdAsync(Guid id);
    Task<bool> UpdateServerAsync(Guid id, UpdateServerDto updateDto);
}

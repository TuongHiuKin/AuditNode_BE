using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IServerService
{
    Task<IEnumerable<ServerResponseDto>> GetServersAsync(string[]? labels = null);
    Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids);
    Task<ServerResponseDto?> GetServerByIdAsync(Guid id);
    Task<ServerResponseDto> CreateServerAsync(CreateServerDto createDto);
    Task<bool> UpdateServerAsync(Guid id, UpdateServerDto updateDto);
}

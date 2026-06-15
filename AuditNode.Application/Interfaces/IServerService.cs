using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IServerService
{
    Task<IEnumerable<ServerResponseDto>> GetAllAsync(string? environment = null, Guid? datacenterId = null);
    Task<IEnumerable<ServerResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task<ServerDetailDto?> GetByIdAsync(Guid id);
    Task<ServerResponseDto> CreateAsync(CreateServerDto createDto);
    Task<bool> UpdateAsync(Guid id, UpdateServerDto updateDto);
}

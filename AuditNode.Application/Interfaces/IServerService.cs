using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IServerService
{
    Task<IEnumerable<ServerResponseDto>> GetAllAsync(string? environment = null, Guid? datacenterId = null);
    Task<ServerDetailDto?> GetByIdAsync(Guid id);
    Task<ServerResponseDto> CreateAsync(CreateServerDto createDto);
    Task<bool> UpdateAsync(Guid id, UpdateServerDto updateDto);
}

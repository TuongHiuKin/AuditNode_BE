using AuditNode.Application.DTOs;
using AuditNode.Domain.Entities;

namespace AuditNode.Application.Interfaces;

public interface IApplicationService
{
    Task<IEnumerable<ApplicationResponseDto>> GetAllAsync();
    Task<ApplicationResponseDto?> GetByIdAsync(Guid id);
    Task<ApplicationResponseDto> CreateAsync(CreateApplicationDto createDto);
    Task<bool> UpdateAsync(Guid id, UpdateApplicationDto updateDto);
}

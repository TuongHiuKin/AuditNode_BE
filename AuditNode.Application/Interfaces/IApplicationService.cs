using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IApplicationService
{
    Task<IEnumerable<ApplicationResponseDto>> GetAllAsync(string? labelKey = null, string? labelValue = null);
    Task<IEnumerable<ApplicationResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task<ApplicationResponseDto?> GetByIdAsync(Guid id);
    Task<ApplicationOperationResult> CreateAsync(CreateApplicationDto createDto);
    Task<ApplicationOperationResult> UpdateAsync(Guid id, UpdateApplicationDto updateDto);
}

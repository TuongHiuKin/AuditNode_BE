using AuditNode.Application.DTOs;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Application.Interfaces;

public interface IApplicationRepository
{
    Task<IEnumerable<ApplicationResponseDto>> GetApplicationsAsync();
    Task<AppEntity?> GetByIdAsync(Guid id);
    Task<AppEntity> RegisterApplicationAsync(AppEntity application);
    Task UpdateAsync(AppEntity application);
    Task<bool> UpdateApplicationWithNetworkAsync(Guid id, UpdateApplicationDto updateDto);
}

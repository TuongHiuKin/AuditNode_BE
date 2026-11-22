using AuditNode.Application.DTOs;
using AuditNode.Domain.Entities;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Application.Interfaces;

public interface IApplicationRepository
{
    Task<IEnumerable<ApplicationResponseDto>> GetApplicationsAsync(string? labelKey = null, string? labelValue = null);
    Task<IEnumerable<ApplicationResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task<IEnumerable<ApplicationResponseDto>> GetScopedAsync(IReadOnlySet<Guid>? applicationIds, IReadOnlySet<Guid>? serverIds, IEnumerable<Guid>? requestedIds = null, string? labelKey = null, string? labelValue = null);
    Task<AppEntity?> GetByIdAsync(Guid id);
    Task<bool> AppCodeExistsAsync(string appCode, string ownerUserId, Guid? excludeApplicationId = null);
    Task<bool> ServerExistsAsync(Guid serverId, string ownerUserId);
    Task<bool> PortCollisionExistsAsync(Guid serverId, int portNumber, string ownerUserId, Guid? excludePortMappingId = null);
    Task<PortMapping?> GetPortMappingAsync(Guid portMappingId);
    Task<AppEntity> CreateAsync(
        AppEntity application,
        IReadOnlyCollection<LabelDto> labels,
        PortMapping? deployment);
    Task UpdateAsync(
        AppEntity application,
        IReadOnlyCollection<LabelDto>? labels,
        PortMapping? deployment);
}

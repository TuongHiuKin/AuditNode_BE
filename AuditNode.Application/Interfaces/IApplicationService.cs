using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IApplicationService
{
    Task<IEnumerable<ApplicationResponseDto>> GetAllAsync(string? labelKey = null, string? labelValue = null);
    Task<CursorPageDto<ApplicationResponseDto>> GetCatalogPageAsync(CatalogPageQuery query, string? labelKey = null, string? labelValue = null, string? ownerUserId = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<ApplicationResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task<ApplicationResponseDto?> GetByIdAsync(Guid id);
    Task<ApplicationResponseDto?> GetCatalogDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApplicationOperationResult> CreateAsync(CreateApplicationDto createDto);
    Task<ApplicationOperationResult> UpdateAsync(Guid id, UpdateApplicationDto updateDto);
    Task<IReadOnlyList<ApplicationResponseDto>> ExportCatalogAsync(IReadOnlyCollection<Guid> ids, CatalogView view, CancellationToken cancellationToken = default);
}

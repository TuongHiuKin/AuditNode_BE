using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ILabelCatalogService
{
    Task<CursorPageDto<CatalogLabelDto>> GetLabelsAsync(CatalogPageQuery query, string? ownerUserId = null, string? labelKey = null, string? labelValue = null, CancellationToken cancellationToken = default);
}

using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ILabelCatalogService
{
    Task<CursorPageDto<CatalogLabelDto>> GetLabelsAsync(CatalogPageQuery query, CancellationToken cancellationToken = default);
}

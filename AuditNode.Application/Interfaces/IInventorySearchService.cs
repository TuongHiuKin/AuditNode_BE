using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IInventorySearchService
{
    Task<IEnumerable<SearchResultDto>> SearchAsync(string keyword);
    Task<CursorPageDto<SearchResultDto>> SearchAsync(string keyword, CatalogPageQuery query, string? ownerUserId = null, string? labelKey = null, string? labelValue = null, CancellationToken cancellationToken = default);
}

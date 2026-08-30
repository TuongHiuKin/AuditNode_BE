using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IShareCatalogService
{
    Task<CursorPageDto<ShareCatalogItemDto>?> BrowseAsync(BrowseShareLinkDto request, CancellationToken cancellationToken = default);
}

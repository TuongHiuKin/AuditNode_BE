using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;

namespace AuditNode.Infrastructure.Services;

public sealed class ShareCatalogService(
    IShareTokenService shareTokens,
    IGlobalCatalogRepository catalog,
    TimeProvider timeProvider) : IShareCatalogService
{
    public async Task<CursorPageDto<ShareCatalogItemDto>?> BrowseAsync(
        BrowseShareLinkDto request,
        CancellationToken cancellationToken = default)
    {
        var scope = await shareTokens.ResolveAsync(request.Token, cancellationToken);
        if (scope is null) return null;
        var query = CatalogPageQuery.Parse("shared", request.Limit, request.Cursor);
        return await catalog.BrowseShareAsync(
            scope,
            request.ResourceType,
            query,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }
}

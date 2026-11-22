using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public class InventorySearchService : IInventorySearchService
{
    private readonly ICurrentUserService _currentUser;
    private readonly IGlobalCatalogRepository _catalog;
    private readonly TimeProvider _timeProvider;

    public InventorySearchService(ICurrentUserService currentUser, IGlobalCatalogRepository catalog, TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _catalog = catalog;
        _timeProvider = timeProvider;
    }

    public Task<CursorPageDto<SearchResultDto>> SearchAsync(string keyword, CatalogPageQuery query, string? ownerUserId = null, string? labelKey = null, string? labelValue = null, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult(new CursorPageDto<SearchResultDto>([], null, false))
            : _catalog.SearchAsync(_currentUser.UserId!, keyword, query, _timeProvider.GetUtcNow().UtcDateTime, ownerUserId, labelKey, labelValue, cancellationToken);

    public async Task<IEnumerable<SearchResultDto>> SearchAsync(string keyword) =>
        string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2
            ? []
            : (await SearchAsync(keyword, new CatalogPageQuery(CatalogView.Mine, 20))).Items;
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;

namespace AuditNode.Infrastructure.Services;

public sealed class LabelCatalogService(
    IGlobalCatalogRepository catalog,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : ILabelCatalogService
{
    public Task<CursorPageDto<CatalogLabelDto>> GetLabelsAsync(
        CatalogPageQuery query,
        string? ownerUserId = null,
        string? labelKey = null,
        string? labelValue = null,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(currentUser.UserId)
            ? Task.FromResult(new CursorPageDto<CatalogLabelDto>([], null, false))
            : catalog.GetLabelsAsync(currentUser.UserId!, query, timeProvider.GetUtcNow().UtcDateTime, ownerUserId, labelKey, labelValue, cancellationToken);
}

using AuditNode.Application.Exceptions;

namespace AuditNode.Application.DTOs;

public sealed record CatalogPageQuery(CatalogView View = CatalogView.Mine, int Limit = 25, string? Cursor = null)
{
    public static CatalogPageQuery Parse(string? view, int? limit, string? cursor)
    {
        var parsedView = string.IsNullOrWhiteSpace(view)
            ? CatalogView.Mine
            : view.Trim().ToLowerInvariant() switch
            {
                "mine" => CatalogView.Mine,
                "shared" => CatalogView.Shared,
                _ => throw new CatalogQueryValidationException("Catalog view must be 'mine' or 'shared'.")
            };
        var parsedLimit = limit ?? 25;
        if (parsedLimit is < 1 or > 100)
            throw new CatalogQueryValidationException("Catalog limit must be between 1 and 100.");
        return new CatalogPageQuery(parsedView, parsedLimit, string.IsNullOrWhiteSpace(cursor) ? null : cursor);
    }
}

public sealed record CursorPageDto<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasNextPage);

public sealed record CatalogCursorPosition(IReadOnlyList<string> SortValues, Guid Id);

public sealed class CatalogLabelDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool IsProtected { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public LabelEffectivePermission EffectivePermission { get; set; }
    public IReadOnlyList<Guid> SharedLabelIds { get; set; } = [];
    public LabelAccessCapabilities Capabilities { get; set; } = CatalogCapabilities.None;
}

public static class CatalogCapabilities
{
    public static LabelAccessCapabilities None { get; } = new(false, false, false, false, false, false, false);
    public static LabelAccessCapabilities Owner { get; } = new(true, true, true, true, true, false, true);
    public static LabelAccessCapabilities Editor { get; } = new(true, true, false, false, false, false, false);
    public static LabelAccessCapabilities Viewer { get; } = new(true, false, false, false, false, false, false);
    public static LabelAccessCapabilities ReadOnly { get; } = new(true, false, false, false, false, false, false);
}

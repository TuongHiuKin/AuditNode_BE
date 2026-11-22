namespace AuditNode.Application.DTOs;

public class SearchResultDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty; // "SERVER" or "APP"
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string MatchReason { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public LabelEffectivePermission EffectivePermission { get; set; }
    public IReadOnlyList<Guid> SharedLabelIds { get; set; } = [];
    public LabelAccessCapabilities Capabilities { get; set; } = CatalogCapabilities.None;
}

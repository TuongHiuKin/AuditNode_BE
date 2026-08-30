namespace AuditNode.Application.DTOs;

public class DatacenterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public LabelEffectivePermission EffectivePermission { get; set; }
    public IReadOnlyList<Guid> SharedLabelIds { get; set; } = [];
    public LabelAccessCapabilities Capabilities { get; set; } = CatalogCapabilities.None;
}

namespace AuditNode.Application.DTOs;

public class WorkspaceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Relationship { get; set; } = string.Empty;
    public string EffectiveRole { get; set; } = string.Empty;
    public WorkspaceScopeDto Scope { get; set; } = new("all", [], []);
    public WorkspaceCapabilitiesDto Capabilities { get; set; } = new(false, false, false, false, false, false);
}

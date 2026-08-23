namespace AuditNode.Domain.Entities;

public class WorkspaceMemberScope
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedByUserId { get; set; } = string.Empty;
    public WorkspaceMember? Member { get; set; }
}

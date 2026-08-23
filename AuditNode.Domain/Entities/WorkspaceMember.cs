namespace AuditNode.Domain.Entities;

public class WorkspaceMember
{
    public Guid WorkspaceId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ScopeMode { get; set; } = WorkspaceScopeModes.All;
    public long Version { get; set; }
    public string InvitedByUserId { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public ICollection<WorkspaceMemberScope> Scopes { get; set; } = new List<WorkspaceMemberScope>();
}

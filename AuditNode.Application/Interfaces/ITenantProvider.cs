namespace AuditNode.Application.Interfaces;

public interface ITenantProvider
{
    Guid? WorkspaceId { get; set; }
    void SetWorkspaceId(string? workspaceIdStr);
}

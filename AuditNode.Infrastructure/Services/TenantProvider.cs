using AuditNode.Application.Interfaces;

namespace AuditNode.Infrastructure.Services;

public class TenantProvider : ITenantProvider
{
    public Guid? WorkspaceId { get; set; }

    public void SetWorkspaceId(string? workspaceIdStr)
    {
        if (Guid.TryParse(workspaceIdStr, out var guid))
        {
            WorkspaceId = guid;
            return;
        }

        WorkspaceId = null;
    }
}

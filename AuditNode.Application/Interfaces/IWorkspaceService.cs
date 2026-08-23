using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IWorkspaceService
{
    Task<IEnumerable<WorkspaceDto>> GetUserWorkspacesAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid workspaceId);
    Task<bool> UserHasAccessAsync(Guid workspaceId, string userId);
}

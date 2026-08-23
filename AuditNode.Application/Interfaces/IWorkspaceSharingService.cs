using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IWorkspaceSharingService
{
    Task<IReadOnlyList<WorkspaceShareDto>?> ListAsync(Guid workspaceId, string actorUserId, CancellationToken cancellationToken = default);
    Task<WorkspaceShareResult> GrantAsync(Guid workspaceId, string actorUserId, UpsertWorkspaceShareDto request, CancellationToken cancellationToken = default);
    Task<WorkspaceShareResult> UpdateAsync(Guid workspaceId, string actorUserId, string userId, UpsertWorkspaceShareDto request, CancellationToken cancellationToken = default);
    Task<WorkspaceShareResult> RevokeAsync(Guid workspaceId, string actorUserId, string userId, long version, CancellationToken cancellationToken = default);
}

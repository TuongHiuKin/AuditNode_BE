using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IWorkspaceAccessService
{
    Task<WorkspaceAccessDto?> ResolveAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default);
}

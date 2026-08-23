using AuditNode.Domain.Entities;

namespace AuditNode.Application.Interfaces;

public interface IWorkspaceRepository
{
    Task<IEnumerable<Workspace>> GetAllAsync();
    Task<IEnumerable<Workspace>> GetAccessibleAsync(string userId);
    Task<bool> ExistsAsync(Guid workspaceId);
    Task<bool> UserHasAccessAsync(Guid workspaceId, string userId);
    Task<Workspace> EnsurePersonalAsync(string userId, CancellationToken cancellationToken = default);
}

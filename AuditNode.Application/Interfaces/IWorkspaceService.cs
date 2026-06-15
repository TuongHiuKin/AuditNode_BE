using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IWorkspaceService
{
    Task<IEnumerable<WorkspaceDto>> GetUserWorkspacesAsync(string userId);
}

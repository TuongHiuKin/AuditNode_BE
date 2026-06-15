using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;

namespace AuditNode.Application.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceRepository _workspaceRepository;

    public WorkspaceService(IWorkspaceRepository workspaceRepository)
    {
        _workspaceRepository = workspaceRepository;
    }

    public async Task<IEnumerable<WorkspaceDto>> GetUserWorkspacesAsync(string userId)
    {
        // For Phase 1, we return all workspaces. 
        // In Phase 2+, we would filter by userId and their permissions/groups.
        var workspaces = await _workspaceRepository.GetAllAsync();
        
        return workspaces.Select(w => new WorkspaceDto
        {
            Id = w.Id,
            Name = w.Name,
            Description = w.Description
        });
    }
}

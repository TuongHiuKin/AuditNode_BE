using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;

namespace AuditNode.Infrastructure.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceRepository _repository;

    public WorkspaceService(IWorkspaceRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<WorkspaceDto>> GetUserWorkspacesAsync(string userId)
    {
        var workspaces = await _repository.GetAccessibleAsync(userId);
        
        return workspaces.Select(w => new WorkspaceDto
        {
            Id = w.Id,
            Name = w.Name,
            Description = w.Description
        });
    }

    public Task<bool> ExistsAsync(Guid workspaceId) => _repository.ExistsAsync(workspaceId);

    public Task<bool> UserHasAccessAsync(Guid workspaceId, string userId) =>
        _repository.UserHasAccessAsync(workspaceId, userId);
}

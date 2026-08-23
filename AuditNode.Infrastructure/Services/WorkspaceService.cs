using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;

namespace AuditNode.Infrastructure.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceRepository _repository;

    public WorkspaceService(IWorkspaceRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<WorkspaceDto>> GetUserWorkspacesAsync(string userId, CancellationToken cancellationToken = default)
    {
        await _repository.EnsurePersonalAsync(userId, cancellationToken);
        var workspaces = await _repository.GetAccessibleAsync(userId);
        
        return workspaces.Select(w =>
        {
            var member = w.Members.SingleOrDefault(item => item.UserId == userId);
            var role = w.OwnerUserId == userId ? WorkspaceRoles.Owner : member?.Role ?? WorkspaceRoles.Viewer;
            var mode = w.OwnerUserId == userId ? WorkspaceScopeModes.All : member?.ScopeMode ?? WorkspaceScopeModes.All;
            var canAdmin = role is WorkspaceRoles.Owner or WorkspaceRoles.Admin;
            var canWrite = canAdmin || role == WorkspaceRoles.Auditor;
            var labels = member?.Scopes.Where(scope => scope.ScopeType == WorkspaceScopeTypes.Label)
                .Select(scope => new WorkspaceScopeTargetDto(scope.TargetId, string.Empty)).ToList() ?? [];
            var frames = member?.Scopes.Where(scope => scope.ScopeType == WorkspaceScopeTypes.Frame)
                .Select(scope => new WorkspaceScopeTargetDto(scope.TargetId, string.Empty)).ToList() ?? [];
            return new WorkspaceDto
            {
                Id = w.Id,
                Name = w.Name,
                Description = w.Description,
                Relationship = w.OwnerUserId == userId ? "owner" : role == WorkspaceRoles.Admin ? "admin" : "shared",
                EffectiveRole = role,
                Scope = new WorkspaceScopeDto(mode, labels, frames),
                Capabilities = new WorkspaceCapabilitiesDto(canAdmin, canWrite, canWrite, canAdmin, canAdmin, canAdmin)
            };
        });
    }

    public Task<bool> ExistsAsync(Guid workspaceId) => _repository.ExistsAsync(workspaceId);

    public Task<bool> UserHasAccessAsync(Guid workspaceId, string userId) =>
        _repository.UserHasAccessAsync(workspaceId, userId);
}

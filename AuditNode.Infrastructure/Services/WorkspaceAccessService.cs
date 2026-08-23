using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public sealed class WorkspaceAccessService(AuditDbContext context) : IWorkspaceAccessService
{
    public async Task<WorkspaceAccessDto?> ResolveAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default)
    {
        var workspace = await context.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == workspaceId, cancellationToken);
        if (workspace is null) return null;

        if (workspace.OwnerUserId == userId)
            return Build(workspaceId, "owner", WorkspaceRoles.Owner, WorkspaceScopeModes.All, []);

        var member = await context.WorkspaceMembers.AsNoTracking().Include(item => item.Scopes)
            .SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.UserId == userId, cancellationToken);
        return member is null ? null : Build(workspaceId, member.Role == WorkspaceRoles.Admin ? "admin" : "shared", member.Role, member.ScopeMode, member.Scopes);
    }

    private static WorkspaceAccessDto Build(Guid workspaceId, string relationship, string role, string mode, IEnumerable<WorkspaceMemberScope> scopes)
    {
        var canAdmin = role is WorkspaceRoles.Owner or WorkspaceRoles.Admin;
        var canWrite = canAdmin || role == WorkspaceRoles.Auditor;
        return new WorkspaceAccessDto(workspaceId, relationship, role,
            new WorkspaceScopeDto(mode,
                scopes.Where(x => x.ScopeType == WorkspaceScopeTypes.Label).Select(x => new WorkspaceScopeTargetDto(x.TargetId, string.Empty)).ToList(),
                scopes.Where(x => x.ScopeType == WorkspaceScopeTypes.Frame).Select(x => new WorkspaceScopeTargetDto(x.TargetId, string.Empty)).ToList()),
            new WorkspaceCapabilitiesDto(canAdmin, canWrite, canWrite, canAdmin, canAdmin, canAdmin));
    }
}

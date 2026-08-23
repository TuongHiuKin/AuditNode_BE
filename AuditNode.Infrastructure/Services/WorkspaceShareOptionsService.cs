using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public sealed class WorkspaceShareOptionsService(AuditDbContext context, IIdentityAdminService identities) : IWorkspaceShareOptionsService
{
    public async Task<WorkspaceShareOptionsDto?> GetAsync(Guid workspaceId, string actorUserId, string? search, int first, int max, CancellationToken cancellationToken = default)
    {
        var canManage = await context.Workspaces.AsNoTracking().AnyAsync(x => x.Id == workspaceId &&
            (x.OwnerUserId == actorUserId || x.Members.Any(m => m.UserId == actorUserId && m.Role == WorkspaceRoles.Admin)), cancellationToken);
        if (!canManage) return null;
        var users = (await identities.ListUsersAsync(search, first, max, cancellationToken)).Where(x => x.Enabled)
            .Select(x => new ShareOptionUserDto(x.Id, x.Username, x.Email)).ToList();
        var labels = await context.Labels.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Key).ThenBy(x => x.Value)
            .Select(x => new ShareOptionTargetDto(x.Id, x.Key + ":" + x.Value)).ToListAsync(cancellationToken);
        var frames = await context.TopologyNodes.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspaceId && x.NodeType == "frame").OrderBy(x => x.Label)
            .Select(x => new ShareOptionTargetDto(x.Id, x.Label)).ToListAsync(cancellationToken);
        return new(users, labels, frames);
    }
}

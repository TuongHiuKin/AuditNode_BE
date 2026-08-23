using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AuditNode.Infrastructure.Services;

public sealed class WorkspaceSharingService(AuditDbContext context, ILogger<WorkspaceSharingService> logger, IIdentityAdminService identities) : IWorkspaceSharingService
{
    public async Task<IReadOnlyList<WorkspaceShareDto>?> ListAsync(Guid workspaceId, string actorUserId, CancellationToken cancellationToken = default)
    {
        if (!await CanManageAsync(workspaceId, actorUserId, cancellationToken)) return null;
        return await context.WorkspaceMembers.AsNoTracking().Where(x => x.WorkspaceId == workspaceId)
            .Select(x => new WorkspaceShareDto(x.UserId, x.Role, x.ScopeMode, x.Scopes.Select(s => s.TargetId).ToList(), x.Version))
            .ToListAsync(cancellationToken);
    }

    public Task<WorkspaceShareResult> GrantAsync(Guid workspaceId, string actorUserId, UpsertWorkspaceShareDto request, CancellationToken cancellationToken = default) =>
        SaveAsync(workspaceId, actorUserId, request.UserId, request, true, cancellationToken);

    public Task<WorkspaceShareResult> UpdateAsync(Guid workspaceId, string actorUserId, string userId, UpsertWorkspaceShareDto request, CancellationToken cancellationToken = default) =>
        SaveAsync(workspaceId, actorUserId, userId, request, false, cancellationToken);

    public async Task<WorkspaceShareResult> RevokeAsync(Guid workspaceId, string actorUserId, string userId, long version, CancellationToken cancellationToken = default)
    {
        if (!await CanManageAsync(workspaceId, actorUserId, cancellationToken)) return WorkspaceShareResult.Forbidden();
        var member = await context.WorkspaceMembers.Include(x => x.Scopes).SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, cancellationToken);
        if (member is null) return WorkspaceShareResult.NotFound();
        if (member.Version != version) return WorkspaceShareResult.Conflict("The share was changed by another request.");
        context.WorkspaceMembers.Remove(member);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return WorkspaceShareResult.Conflict("The share was changed by another request."); }
        logger.LogInformation("Workspace share revoked. WorkspaceId={WorkspaceId} ActorUserId={ActorUserId} TargetUserId={TargetUserId}", workspaceId, actorUserId, userId);
        return new WorkspaceShareResult(true);
    }

    private async Task<WorkspaceShareResult> SaveAsync(Guid workspaceId, string actorUserId, string userId, UpsertWorkspaceShareDto request, bool create, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(workspaceId, actorUserId, cancellationToken)) return WorkspaceShareResult.Forbidden();
        var workspace = await context.Workspaces.SingleOrDefaultAsync(x => x.Id == workspaceId, cancellationToken);
        if (workspace is null) return WorkspaceShareResult.NotFound();
        if (userId != request.UserId || userId == workspace.OwnerUserId) return WorkspaceShareResult.Invalid("The share target is invalid.");
        var identity = await identities.GetUserAsync(userId, cancellationToken);
        if (identity is null || !identity.Enabled) return WorkspaceShareResult.Invalid("The share target must be an active user.");
        var validation = await ValidateAsync(workspaceId, request, cancellationToken);
        if (validation is not null) return WorkspaceShareResult.Invalid(validation);

        var member = await context.WorkspaceMembers.Include(x => x.Scopes).SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, cancellationToken);
        if (create && member is not null) return WorkspaceShareResult.Conflict("The share already exists.");
        if (!create && member is null) return WorkspaceShareResult.NotFound();
        if (!create && member!.Version != request.Version) return WorkspaceShareResult.Conflict("The share was changed by another request.");

        member ??= new WorkspaceMember { WorkspaceId = workspaceId, UserId = userId, InvitedByUserId = actorUserId, JoinedAt = DateTime.UtcNow };
        if (create) context.WorkspaceMembers.Add(member);
        member.Role = request.Role;
        member.ScopeMode = request.ScopeMode;
        member.Version++;
        context.WorkspaceMemberScopes.RemoveRange(member.Scopes);
        member.Scopes = request.TargetIds.Distinct().Select(id => new WorkspaceMemberScope
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = userId,
            ScopeType = request.ScopeMode == WorkspaceScopeModes.Labels ? WorkspaceScopeTypes.Label : WorkspaceScopeTypes.Frame,
            TargetId = id, CreatedByUserId = actorUserId
        }).ToList();
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return WorkspaceShareResult.Conflict("The share was changed by another request."); }
        logger.LogInformation("Workspace share {Action}. WorkspaceId={WorkspaceId} ActorUserId={ActorUserId} TargetUserId={TargetUserId} Role={Role} ScopeMode={ScopeMode}", create ? "granted" : "updated", workspaceId, actorUserId, userId, request.Role, request.ScopeMode);
        return new WorkspaceShareResult(true, Share: new WorkspaceShareDto(userId, member.Role, member.ScopeMode, member.Scopes.Select(x => x.TargetId).ToList(), member.Version));
    }

    private async Task<string?> ValidateAsync(Guid workspaceId, UpsertWorkspaceShareDto request, CancellationToken cancellationToken)
    {
        if (request.Role is not (WorkspaceRoles.Admin or WorkspaceRoles.Auditor or WorkspaceRoles.Viewer)) return "Unsupported workspace role.";
        if (request.Role == WorkspaceRoles.Admin && request.ScopeMode != WorkspaceScopeModes.All) return "Workspace admins require all scope.";
        if (request.ScopeMode == WorkspaceScopeModes.All) return request.TargetIds.Count == 0 ? null : "All scope cannot contain targets.";
        if (request.ScopeMode is not (WorkspaceScopeModes.Labels or WorkspaceScopeModes.Frames) || request.TargetIds.Count == 0) return "Scoped shares require at least one target.";
        var ids = request.TargetIds.Distinct().ToList();
        var valid = request.ScopeMode == WorkspaceScopeModes.Labels
            ? await context.Labels.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == workspaceId && ids.Contains(x.Id), cancellationToken)
            : await context.TopologyNodes.IgnoreQueryFilters().CountAsync(x => x.WorkspaceId == workspaceId && ids.Contains(x.Id) && x.NodeType == "frame", cancellationToken);
        return valid == ids.Count ? null : "One or more scope targets are invalid or belong to another workspace.";
    }

    private Task<bool> CanManageAsync(Guid workspaceId, string userId, CancellationToken cancellationToken) =>
        context.Workspaces.AnyAsync(x => x.Id == workspaceId && (x.OwnerUserId == userId || x.Members.Any(m => m.UserId == userId && m.Role == WorkspaceRoles.Admin)), cancellationToken);
}

using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AuditNode.API.Security;

[AttributeUsage(AttributeTargets.Method)]
public sealed class WorkspaceMutationAttribute(bool ownerOrAdminOnly = false) : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantProvider>();
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserService>();
        var accessService = context.HttpContext.RequestServices.GetRequiredService<IWorkspaceAccessService>();
        if (!tenant.WorkspaceId.HasValue || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            context.Result = new ForbidResult();
            return;
        }

        var access = await accessService.ResolveAsync(tenant.WorkspaceId.Value, currentUser.UserId!, context.HttpContext.RequestAborted);
        var allowed = access is not null && access.EffectiveRole is WorkspaceRoles.Owner or WorkspaceRoles.Admin;
        if (!ownerOrAdminOnly && access is not null && access.EffectiveRole == WorkspaceRoles.Auditor && access.Scope.Mode == WorkspaceScopeModes.Labels)
            allowed = true;
        if (!allowed) context.Result = new ForbidResult();
    }
}

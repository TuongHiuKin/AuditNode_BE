using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AuditNode.API.Security;

[AttributeUsage(AttributeTargets.Method)]
public sealed class WorkspaceGraphMutationAttribute : Attribute, IAsyncAuthorizationFilter
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

        var access = await accessService.ResolveAsync(
            tenant.WorkspaceId.Value,
            currentUser.UserId!,
            context.HttpContext.RequestAborted);
        if (access?.EffectiveRole is not (WorkspaceRoles.Owner or WorkspaceRoles.Admin or WorkspaceRoles.Auditor))
            context.Result = new ForbidResult();
    }
}

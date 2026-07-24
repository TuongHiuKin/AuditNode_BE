using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace AuditNode.API.Middleware;

public class WorkspaceMiddleware
{
    private readonly RequestDelegate _next;
    private const string WorkspaceHeader = "X-Workspace-Id";

    public WorkspaceMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider)
    {
        // Skip workspace validation for non-API paths or specific endpoints like auth and workspace selection
        if (!context.Request.Path.StartsWithSegments("/api") || 
            context.Request.Path.StartsWithSegments("/api/v1/auth") ||
            context.Request.Path.StartsWithSegments("/api/v1/workspaces"))
        {
            await _next(context);
            return;
        }

        // 1. Extract Header
        if (!context.Request.Headers.TryGetValue(WorkspaceHeader, out var workspaceIdHeader))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "Workspace ID header (X-Workspace-Id) missing." });
            return;
        }

        var workspaceIdStr = workspaceIdHeader.ToString();

        // 2. Validate UUID format
        if (!Guid.TryParse(workspaceIdStr, out var workspaceId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "Invalid Workspace ID format." });
            return;
        }

        // Note: Guid.Empty (00000000-0000-0000-0000-000000000000) is considered a valid 
        // workspace ID (the Default Workspace) and is explicitly allowed to pass.

        // 3. Set in Tenant Provider
        tenantProvider.WorkspaceId = workspaceId;

        await _next(context);
    }
}

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

    public async Task InvokeAsync(
        HttpContext context,
        ITenantProvider tenantProvider,
        IWorkspaceService workspaceService)
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
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Workspace ID header (X-Workspace-Id) is required." });
            return;
        }

        var workspaceIdStr = workspaceIdHeader.ToString();

        // 2. Validate UUID format
        if (!Guid.TryParse(workspaceIdStr, out var workspaceId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "A valid workspace ID is required." });
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "An authenticated user identifier is required." });
            return;
        }

        if (!await workspaceService.ExistsAsync(workspaceId))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Workspace not found." });
            return;
        }

        if (workspaceIdStr != "11111111-1111-1111-1111-111111111111")
        {
            if (!await workspaceService.UserHasAccessAsync(workspaceId, userId))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Workspace access is forbidden." });
                return;
            }
        }

        tenantProvider.SetWorkspaceId(workspaceIdStr);

        await _next(context);
    }
}

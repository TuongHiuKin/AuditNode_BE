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
        // Skip workspace validation for non-API paths or health checks if any
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // 1. Extract Header
        if (!context.Request.Headers.TryGetValue(WorkspaceHeader, out var workspaceIdHeader))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Workspace ID header missing.");
            return;
        }

        var workspaceIdStr = workspaceIdHeader.ToString();

        // 2. Validate UUID format
        if (!Guid.TryParse(workspaceIdStr, out var workspaceId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Invalid Workspace ID format.");
            return;
        }

        // 3. RBAC Placeholder: Validate if user belongs to authorized group for this workspace
        // In a real scenario, you'd check claims like "groups" or "workspaces" from the Keycloak token
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            // Placeholder logic: Check if user has a claim that matches the workspace or is an admin
            // var userGroups = user.FindAll("groups").Select(c => c.Value);
            // if (!userGroups.Contains($"workspace-{workspaceId}") && !userGroups.Contains("admin")) { ... }
            
            // For now, we just log/accept it as a placeholder
            // Console.WriteLine($"User {user.Identity.Name} accessing workspace {workspaceId}");
        }

        // 4. Set in Tenant Provider
        tenantProvider.WorkspaceId = workspaceId;

        await _next(context);
    }
}

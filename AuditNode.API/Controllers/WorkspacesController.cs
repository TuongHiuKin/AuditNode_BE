using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route(ApiRoutes.BaseRoute)]
public class WorkspacesController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;

    public WorkspacesController(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkspaceDto>>> GetWorkspaces()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { error = "An authenticated user identifier is required." });
        }

        var result = await _workspaceService.GetUserWorkspacesAsync(userId);
        return Ok(result);
    }
}

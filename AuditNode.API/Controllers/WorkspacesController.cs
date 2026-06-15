using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
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
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        var workspaces = await _workspaceService.GetUserWorkspacesAsync(userId);
        return Ok(workspaces);
    }
}

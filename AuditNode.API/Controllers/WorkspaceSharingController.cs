using System.Security.Claims;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/shares")]
public sealed class WorkspaceSharingController(IWorkspaceSharingService sharingService, IWorkspaceShareOptionsService shareOptions) : ControllerBase
{
    [HttpGet("~/api/v1/workspaces/{workspaceId:guid}/share-options")]
    public async Task<ActionResult<WorkspaceShareOptionsDto>> Options(Guid workspaceId, [FromQuery] string? search, [FromQuery] int first = 0, [FromQuery] int max = 20, CancellationToken cancellationToken = default)
    {
        if (first < 0 || max is < 1 or > 100) return BadRequest();
        var result = await shareOptions.GetAsync(workspaceId, Actor(), search, first, max, cancellationToken);
        return result is null ? Forbid() : Ok(result);
    }
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkspaceShareDto>>> List(Guid workspaceId, CancellationToken cancellationToken)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await sharingService.ListAsync(workspaceId, actor, cancellationToken);
        return result is null ? Forbid() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<WorkspaceShareDto>> Grant(Guid workspaceId, UpsertWorkspaceShareDto request, CancellationToken cancellationToken) =>
        ToActionResult(await sharingService.GrantAsync(workspaceId, Actor(), request, cancellationToken), true);

    [HttpPut("{userId}")]
    public async Task<ActionResult<WorkspaceShareDto>> Update(Guid workspaceId, string userId, UpsertWorkspaceShareDto request, CancellationToken cancellationToken) =>
        ToActionResult(await sharingService.UpdateAsync(workspaceId, Actor(), userId, request, cancellationToken), false);

    [HttpDelete("{userId}")]
    public async Task<IActionResult> Revoke(Guid workspaceId, string userId, [FromQuery] long? version, CancellationToken cancellationToken)
    {
        if (!version.HasValue) return BadRequest(new { error = "A share version is required." });
        var result = await sharingService.RevokeAsync(workspaceId, Actor(), userId, version.Value, cancellationToken);
        if (result.Success) return NoContent();
        return result.ErrorCode switch { "forbidden" => Forbid(), "not_found" => NotFound(), "conflict" => Conflict(new { error = result.Error }), _ => BadRequest(new { error = result.Error }) };
    }

    private string Actor() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    private ActionResult<WorkspaceShareDto> ToActionResult(WorkspaceShareResult result, bool created)
    {
        if (result.Success) return created ? StatusCode(StatusCodes.Status201Created, result.Share) : Ok(result.Share);
        return result.ErrorCode switch
        {
            "forbidden" => Forbid(),
            "not_found" => NotFound(),
            "conflict" => Conflict(new { error = result.Error }),
            _ => BadRequest(new { error = result.Error })
        };
    }
}

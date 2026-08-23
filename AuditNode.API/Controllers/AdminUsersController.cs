using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AuditNode.Application.DTOs;

namespace AuditNode.API.Controllers;

[Authorize(Policy = "SystemAdminOnly")]
[ApiController]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(IIdentityAdminService identities, IWorkspaceUserSummaryService summaries, ILogger<AdminUsersController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IdentityAdminUserDto>>> List([FromQuery] string? search, [FromQuery] int first = 0, [FromQuery] int max = 50, CancellationToken cancellationToken = default)
    {
        if (first < 0 || max is < 1 or > 100) return BadRequest();
        var users = await identities.ListUsersAsync(search, first, max, cancellationToken);
        var ids = users.Select(x => x.Id).ToList();
        var counts = await summaries.GetWorkspaceCountsAsync(ids, cancellationToken);
        return Ok(users.Select(user => user with { WorkspaceCount = counts.GetValueOrDefault(user.Id) }).ToList());
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> Status(string id, [FromBody] UpdateIdentityStatus request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            await identities.SetEnabledAsync(id, request.Enabled, cancellationToken);
            logger.LogInformation("Identity status changed. TargetUserId={TargetUserId} Enabled={Enabled}", id, request.Enabled);
            return NoContent();
        }
        catch (IdentityConflictException) { return Conflict(new { error = "The last SystemAdmin cannot be disabled." }); }
        catch (IdentityConfigurationException) { return StatusCode(500, new { error = "Identity administration is not configured correctly." }); }
        catch (IdentityUpstreamUnavailableException) { return StatusCode(503, new { error = "Identity administration is unavailable." }); }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateIdentityAdminUserDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password)) return BadRequest();
        try
        {
            await identities.CreateUserAsync(request, cancellationToken);
            logger.LogInformation("System administrator created identity. Username={Username}", request.Username);
            return StatusCode(StatusCodes.Status201Created);
        }
        catch (IdentityConflictException) { return Conflict(new { error = "The username or email already exists." }); }
        catch (IdentityConfigurationException) { return StatusCode(500, new { error = "Identity administration is not configured correctly." }); }
        catch (IdentityUpstreamUnavailableException) { return StatusCode(503, new { error = "Identity administration is unavailable." }); }
    }

    [HttpPut("{id}/roles")]
    public async Task<IActionResult> Roles(string id, [FromBody] UpdateSystemAdminRole request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            await identities.SetSystemAdminAsync(id, request.SystemAdmin, cancellationToken);
            logger.LogInformation("System role changed. TargetUserId={TargetUserId} SystemAdmin={SystemAdmin}", id, request.SystemAdmin);
            return NoContent();
        }
        catch (IdentityConflictException) { return Conflict(new { error = "The last SystemAdmin cannot be revoked." }); }
        catch (IdentityConfigurationException) { return StatusCode(500, new { error = "Identity administration is not configured correctly." }); }
        catch (IdentityUpstreamUnavailableException) { return StatusCode(503, new { error = "Identity administration is unavailable." }); }
    }
}

public sealed record UpdateIdentityStatus(bool Enabled);
public sealed record UpdateSystemAdminRole(bool SystemAdmin);

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
        using var auditScope = BeginMutationScope(id, request.Enabled ? "enable_identity" : "disable_identity");
        try
        {
            await identities.SetEnabledAsync(id, request.Enabled, cancellationToken);
            logger.LogInformation("Identity administration mutation completed. Enabled={Enabled}", request.Enabled);
            return NoContent();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { logger.LogWarning("Identity administration request canceled; destructive mutations complete safety verification before cancellation propagates."); throw; }
        catch (IdentityConflictException exception) { logger.LogWarning(exception, "Identity administration mutation rejected by invariant."); return Conflict(new { error = "The last SystemAdmin cannot be disabled." }); }
        catch (IdentityProtectedException exception) { logger.LogWarning(exception, "Protected identity mutation rejected."); return Conflict(new { error = "The emergency SystemAdmin cannot be modified through AuditNode." }); }
        catch (IdentityMutationLockUnavailableException exception) { return MutationUnavailable(exception, "Identity administration is busy. Retry later."); }
        catch (IdentityInvariantViolationException exception) { logger.LogCritical(exception, "Identity administration mutation required recovery."); return StatusCode(503, new { error = "Identity administration could not verify the SystemAdmin safety invariant." }); }
        catch (IdentityConfigurationException exception) { logger.LogError(exception, "Identity administration configuration failure."); return StatusCode(500, new { error = "Identity administration is not configured correctly." }); }
        catch (IdentityUpstreamUnavailableException exception) { logger.LogWarning(exception, "Identity administration upstream unavailable."); return StatusCode(503, new { error = "Identity administration is unavailable." }); }
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
        using var auditScope = BeginMutationScope(id, request.SystemAdmin ? "grant_system_admin" : "revoke_system_admin");
        try
        {
            await identities.SetSystemAdminAsync(id, request.SystemAdmin, cancellationToken);
            logger.LogInformation("Identity administration mutation completed. SystemAdmin={SystemAdmin}", request.SystemAdmin);
            return NoContent();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { logger.LogWarning("Identity administration request canceled; destructive mutations complete safety verification before cancellation propagates."); throw; }
        catch (IdentityConflictException exception) { logger.LogWarning(exception, "Identity administration mutation rejected by invariant."); return Conflict(new { error = "The last SystemAdmin cannot be revoked." }); }
        catch (IdentityProtectedException exception) { logger.LogWarning(exception, "Protected identity mutation rejected."); return Conflict(new { error = "The emergency SystemAdmin cannot be modified through AuditNode." }); }
        catch (IdentityMutationLockUnavailableException exception) { return MutationUnavailable(exception, "Identity administration is busy. Retry later."); }
        catch (IdentityInvariantViolationException exception) { logger.LogCritical(exception, "Identity administration mutation required recovery."); return StatusCode(503, new { error = "Identity administration could not verify the SystemAdmin safety invariant." }); }
        catch (IdentityConfigurationException exception) { logger.LogError(exception, "Identity administration configuration failure."); return StatusCode(500, new { error = "Identity administration is not configured correctly." }); }
        catch (IdentityUpstreamUnavailableException exception) { logger.LogWarning(exception, "Identity administration upstream unavailable."); return StatusCode(503, new { error = "Identity administration is unavailable." }); }
    }

    private IDisposable? BeginMutationScope(string targetUserId, string action) => logger.BeginScope(
        new Dictionary<string, object?>
        {
            ["ActorUserId"] = ControllerContext.HttpContext?.User.FindFirst("sub")?.Value ?? "unknown",
            ["TargetUserId"] = targetUserId,
            ["Action"] = action,
            ["CorrelationId"] = ControllerContext.HttpContext?.TraceIdentifier ?? "unavailable"
        });

    private IActionResult MutationUnavailable(Exception exception, string error)
    {
        logger.LogWarning(exception, "Identity administration distributed lock unavailable.");
        if (ControllerContext.HttpContext is not null) Response.Headers.RetryAfter = "5";
        return StatusCode(503, new { error });
    }
}

public sealed record UpdateIdentityStatus(bool Enabled);
public sealed record UpdateSystemAdminRole(bool SystemAdmin);

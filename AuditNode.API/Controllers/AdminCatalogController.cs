using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

/// <summary>
/// SystemAdmin-only, read-only inspection of a user's catalog.
///
/// There is deliberately no write verb here: an administrator can audit what a user
/// recorded and cannot alter it. Sharing permissions are managed separately under
/// <c>/admin/users/{id}/grants</c>.
/// </summary>
[Authorize(Policy = "SystemAdminOnly")]
[ApiController]
[Route("api/v1/admin/users/{ownerUserId}/catalog")]
public sealed class AdminCatalogController(IAdminCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdminOwnerCatalogDto>> Get(
        string ownerUserId, CancellationToken cancellationToken)
    {
        var result = await catalog.GetOwnerCatalogAsync(ownerUserId, cancellationToken);
        return result is null
            ? NotFound(new ProblemDetails { Status = 404, Title = "The user was not found." })
            : Ok(result);
    }
}

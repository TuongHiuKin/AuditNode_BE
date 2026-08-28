using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.API.Security;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
public class DependenciesController : ControllerBase
{
    private readonly IDependencyService _dependencyService;

    public DependenciesController(IDependencyService dependencyService)
    {
        _dependencyService = dependencyService;
    }

    [WorkspaceMutation(ownerOrAdminOnly: true)]
    [HttpPut("sync")]
    public async Task<IActionResult> SyncDependencies([FromBody] SyncDependenciesDto dto)
    {
        if (dto?.Version is null || dto.Dependencies is null)
            return BadRequest(Problem(400, "A version and dependency collection are required."));

        try
        {
            var status = await _dependencyService.SyncDependenciesAsync(dto);
            return status switch
            {
                DependencySyncStatus.Success => NoContent(),
                DependencySyncStatus.NotFound => NotFound(Problem(404, "An application or deployment was not found in the current workspace.")),
                DependencySyncStatus.Duplicate => Conflict(Problem(409, "Duplicate dependencies are not allowed.")),
                DependencySyncStatus.SelfLoop => BadRequest(Problem(400, "An application cannot depend on itself.")),
                DependencySyncStatus.DestinationMismatch => BadRequest(Problem(400, "The destination deployment does not belong to the destination application.")),
                DependencySyncStatus.Forbidden => StatusCode(403, Problem(403, "Dependency synchronization is forbidden.")),
                DependencySyncStatus.Conflict => Conflict(Problem(409, "The topology changed. Refresh and retry.")),
                _ => BadRequest(Problem(400, "Dependency data is invalid."))
            };
        }
        catch (Exception)
        {
            return StatusCode(500, Problem(500, "Dependencies could not be synchronized."));
        }
    }

    private static ProblemDetails Problem(int status, string title) => new() { Status = status, Title = title };
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.API.Security;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
public class InfrastructureController : ControllerBase
{
    private readonly IInfrastructureService _infrastructureService;

    public InfrastructureController(IInfrastructureService infrastructureService)
    {
        _infrastructureService = infrastructureService;
    }

    [HttpGet("apps/{id:guid}/dependencies-count")]
    public async Task<ActionResult<int>> GetDependenciesCount(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            return BadRequest(Problem(400, "A non-empty application identifier is required."));
        try
        {
            var count = await _infrastructureService.GetDependenciesCountCatalogAsync(id, cancellationToken);
            return count.HasValue ? Ok(count.Value) : NotFound(Problem(404, "Application was not found."));
        }
        catch (Exception)
        {
            return Failure("Dependency count could not be retrieved.");
        }
    }

    [HttpPut("apps/migrate")]
    public async Task<IActionResult> MigrateApp([FromBody] MigrateAppDto migrateDto)
    {
        try
        {
            var status = await _infrastructureService.MigrateAppAsync(migrateDto);
            return status switch
            {
                DeploymentOperationStatus.Success => NoContent(),
                DeploymentOperationStatus.InvalidRequest =>
                    BadRequest(Problem(400, "Explicit deployment, server and valid port values are required.")),
                DeploymentOperationStatus.NotFound =>
                    NotFound(Problem(404, "Deployment was not found.")),
                DeploymentOperationStatus.ServerNotFound =>
                    BadRequest(Problem(400, "Target server was not found in the resource owner's catalog.")),
                DeploymentOperationStatus.PortCollision =>
                    Conflict(Problem(409, "The target server port is already assigned.")),
                DeploymentOperationStatus.Forbidden => Forbid(),
                _ => Failure("Deployment could not be migrated.")
            };
        }
        catch (Exception)
        {
            return Failure("Deployment could not be migrated.");
        }
    }

    [HttpDelete("apps/{id:guid}/purge")]
    public async Task<IActionResult> PurgeApp(Guid id)
    {
        if (id == Guid.Empty)
            return BadRequest(Problem(400, "A non-empty application identifier is required."));
        try
        {
            return await _infrastructureService.PurgeAppAsync(id)
                ? NoContent()
                : NotFound(Problem(404, "Application was not found."));
        }
        catch (Exception)
        {
            return Failure("Application could not be purged.");
        }
    }

    [HttpGet("servers/{id:guid}/deployed-apps")]
    public async Task<ActionResult<IEnumerable<DeployedAppDto>>> GetDeployedAppsByServer(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            return BadRequest(Problem(400, "A non-empty server identifier is required."));
        try
        {
            var applications = await _infrastructureService.GetDeployedAppsByServerCatalogAsync(id, cancellationToken);
            return applications is null
                ? NotFound(Problem(404, "Server was not found."))
                : Ok(applications);
        }
        catch (Exception)
        {
            return Failure("Deployed applications could not be retrieved.");
        }
    }

    private static ProblemDetails Problem(int status, string title) => new() { Status = status, Title = title };
    private ObjectResult Failure(string title) => StatusCode(500, Problem(500, title));
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    public async Task<ActionResult<int>> GetDependenciesCount(Guid id)
    {
        try
        {
            var count = await _infrastructureService.GetDependenciesCountAsync(id);
            return Ok(count);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to retrieve dependency count", message = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,Auditor")]
    [HttpPut("apps/migrate")]
    public async Task<IActionResult> MigrateApp([FromBody] MigrateAppDto migrateDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var success = await _infrastructureService.MigrateAppAsync(migrateDto);
            if (!success)
            {
                return NotFound(new { error = "PortMapping not found" });
            }

            return Ok(new { message = "Migration successful" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Migration failed", message = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,Auditor")]
    [HttpDelete("apps/{id:guid}/purge")]
    public async Task<IActionResult> PurgeApp(Guid id)
    {
        try
        {
            var success = await _infrastructureService.PurgeAppAsync(id);
            if (!success)
            {
                return NotFound(new { error = "Application not found" });
            }

            return Ok(new { message = "Application and dependencies purged successfully" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Purge failed", message = ex.Message });
        }
    }

    [HttpGet("servers/{id:guid}/deployed-apps")]
    public async Task<ActionResult<IEnumerable<DeployedAppDto>>> GetDeployedAppsByServer(Guid id)
    {
        try
        {
            var apps = await _infrastructureService.GetDeployedAppsByServerAsync(id);
            return Ok(apps);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to retrieve deployed applications", message = ex.Message });
        }
    }
}

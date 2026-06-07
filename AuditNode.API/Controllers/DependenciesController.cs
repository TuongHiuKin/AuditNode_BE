using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DependenciesController : ControllerBase
{
    private readonly IDependencyService _dependencyService;

    public DependenciesController(IDependencyService dependencyService)
    {
        _dependencyService = dependencyService;
    }

    [HttpPut("sync")]
    public async Task<IActionResult> SyncDependencies([FromBody] SyncDependenciesDto syncDto)
    {
        if (syncDto == null || syncDto.Dependencies == null)
        {
            return BadRequest("Invalid payload.");
        }

        try
        {
            await _dependencyService.SyncDependenciesAsync(syncDto);
            return Ok(new { message = "Dependencies synchronized successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    [Authorize(Roles = "Admin,Auditor")]
    [HttpPut("sync")]
    public async Task<IActionResult> SyncDependencies([FromBody] SyncDependenciesDto dto)
    {
        if (dto == null || dto.Dependencies == null)
        {
            return BadRequest("Invalid dependency data.");
        }

        try
        {
            await _dependencyService.SyncDependenciesAsync(dto);
            return Ok(new { Message = "Dependencies synchronized successfully." });
        }
        catch (Exception ex)
        {
            // In a real scenario, we would log this error
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}

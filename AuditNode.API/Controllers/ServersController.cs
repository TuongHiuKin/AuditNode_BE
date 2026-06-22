using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route(ApiRoutes.BaseRoute)]
public class ServersController : ControllerBase
{
    private readonly IServerService _serverService;
    private readonly ITenantProvider _tenantProvider;

    public ServersController(IServerService serverService, ITenantProvider tenantProvider)
    {
        _serverService = serverService;
        _tenantProvider = tenantProvider;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServerResponseDto>>> GetServers()
    {
        var result = await _serverService.GetServersAsync();
        return Ok(result);
    }

    [HttpGet("export")]
    public async Task<ActionResult<IEnumerable<ServerResponseDto>>> ExportServers([FromQuery] List<Guid> ids)
    {
        if (ids == null || ids.Count == 0)
        {
            return BadRequest(new { error = "No IDs provided for export." });
        }

        var result = await _serverService.ExportServersAsync(ids);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServerResponseDto>> GetServer(Guid id)
    {
        Console.WriteLine($"[DEBUG TRACE] Controller fetching Server ID: {id}. Active Workspace ID from Context: {_tenantProvider.WorkspaceId}");
        var result = await _serverService.GetServerByIdAsync(id);
        
        if (result == null)
        {
            return NotFound();
        }
        
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateServer([FromRoute] Guid id, [FromBody] UpdateServerDto updateDto)
    {
        if (updateDto == null)
        {
            return BadRequest(new { error = "Update data is missing." });
        }

        var success = await _serverService.UpdateServerAsync(id, updateDto);
        
        if (!success)
        {
            return NotFound(new { error = $"Server with ID {id} not found." });
        }

        return NoContent(); // HTTP 204
    }
}

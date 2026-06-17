using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
public class ServersController : ControllerBase
{
    private readonly IServerService _serverService;

    public ServersController(IServerService serverService)
    {
        _serverService = serverService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServerResponseDto>>> GetServers([FromQuery] string? environment, [FromQuery] Guid? datacenterId)
    {
        try
        {
            var servers = await _serverService.GetAllAsync(environment, datacenterId);
            return Ok(servers);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("export")]
    public async Task<ActionResult<IEnumerable<ServerResponseDto>>> ExportServers([FromQuery] Guid[] ids)
    {
        try
        {
            if (ids == null || ids.Length == 0)
            {
                return BadRequest(new { error = "No IDs provided for export." });
            }

            var servers = await _serverService.GetByIdsAsync(ids);
            return Ok(servers);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServerDetailDto>> GetServer(Guid id)
    {
        var server = await _serverService.GetByIdAsync(id);
        if (server == null)
        {
            return NotFound();
        }
        return Ok(server);
    }

    [HttpPost]
    public async Task<ActionResult> PostServer([FromBody] CreateServerDto serverDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var responseDto = await _serverService.CreateAsync(serverDto);
            return CreatedAtAction(nameof(GetServer), new { id = responseDto.Id }, responseDto);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> PutServer(Guid id, [FromBody] UpdateServerDto updateDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _serverService.UpdateAsync(id, updateDto);
            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

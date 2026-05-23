using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServersController : ControllerBase
{
    private readonly IServerRepository _serverRepository;

    public ServersController(IServerRepository serverRepository)
    {
        _serverRepository = serverRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServerResponseDto>>> GetServers([FromQuery] string? environment, [FromQuery] Guid? datacenterId)
    {
        try
        {
            var servers = await _serverRepository.GetAllWithAppsAsync(environment, datacenterId);
            return Ok(servers);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult> PostServer([FromBody] CreateServerDto serverDto)
    {
        try
        {
            var server = new Server
            {
                Id = Guid.NewGuid(),
                DatacenterId = serverDto.DatacenterId,
                IpAddress = serverDto.IpAddress,
                Hostname = serverDto.Hostname,
                OsType = serverDto.OsType,
                Environment = serverDto.Environment,
                Status = serverDto.Status
            };

            await _serverRepository.CreateServerAsync(server);

            return CreatedAtAction(nameof(GetServers), new { id = server.Id }, server);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

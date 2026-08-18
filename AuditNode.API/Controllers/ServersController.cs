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

    public ServersController(IServerService serverService)
    {
        _serverService = serverService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServerResponseDto>>> GetServers()
    {
        try
        {
            var result = await _serverService.GetServersAsync();
            return Ok(result);
        }
        catch (Exception)
        {
            return ServerFailure();
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServerResponseDto>> GetServer(Guid id)
    {
        if (id == Guid.Empty)
            return BadRequest(Problem(400, "A non-empty server identifier is required."));

        try
        {
            var result = await _serverService.GetServerAsync(id);
            return result is null
                ? NotFound(Problem(404, "Server was not found."))
                : Ok(result);
        }
        catch (Exception)
        {
            return ServerFailure();
        }
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Auditor")]
    public async Task<ActionResult<ServerResponseDto>> CreateServer([FromBody] CreateServerDto dto)
    {
        try
        {
            var result = await _serverService.CreateServerAsync(dto);
            return result.Status switch
            {
                ServerOperationStatus.Success when result.Server is not null =>
                    CreatedAtAction(nameof(GetServer), new { id = result.Server.Id }, result.Server),
                ServerOperationStatus.DatacenterNotFound =>
                    BadRequest(Problem(400, "Datacenter was not found in the current workspace.")),
                ServerOperationStatus.DuplicateIp =>
                    Conflict(Problem(409, "A server with this IP address already exists in the current workspace.")),
                ServerOperationStatus.InvalidWorkspace =>
                    BadRequest(Problem(400, "A valid workspace is required.")),
                _ => ServerFailure()
            };
        }
        catch (Exception)
        {
            return ServerFailure();
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Auditor")]
    public async Task<IActionResult> UpdateServer(Guid id, [FromBody] UpdateServerDto dto)
    {
        if (id == Guid.Empty)
            return BadRequest(Problem(400, "A non-empty server identifier is required."));

        try
        {
            var result = await _serverService.UpdateServerAsync(id, dto);
            return result.Status switch
            {
                ServerOperationStatus.Success => NoContent(),
                ServerOperationStatus.NotFound => NotFound(Problem(404, "Server was not found.")),
                ServerOperationStatus.DatacenterNotFound =>
                    BadRequest(Problem(400, "Datacenter was not found in the current workspace.")),
                ServerOperationStatus.DuplicateIp =>
                    Conflict(Problem(409, "A server with this IP address already exists in the current workspace.")),
                ServerOperationStatus.InvalidWorkspace =>
                    BadRequest(Problem(400, "A valid workspace is required.")),
                _ => ServerFailure()
            };
        }
        catch (Exception)
        {
            return ServerFailure();
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Auditor")]
    public async Task<IActionResult> DeleteServer(Guid id)
    {
        if (id == Guid.Empty)
            return BadRequest(Problem(400, "A non-empty server identifier is required."));

        try
        {
            var status = await _serverService.PurgeServerAsync(id);
            return status switch
            {
                ServerOperationStatus.Success => NoContent(),
                ServerOperationStatus.NotFound => NotFound(Problem(404, "Server was not found.")),
                ServerOperationStatus.InvalidWorkspace =>
                    BadRequest(Problem(400, "A valid workspace is required.")),
                _ => ServerFailure()
            };
        }
        catch (Exception)
        {
            return ServerFailure();
        }
    }

    [HttpGet("export")]
    public async Task<ActionResult<IEnumerable<ServerResponseDto>>> ExportServers([FromQuery] List<Guid> ids)
    {
        if (ids == null || ids.Count == 0 || ids.Any(id => id == Guid.Empty))
        {
            return BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Non-empty server identifiers are required."
            });
        }

        try
        {
            var result = await _serverService.ExportServersAsync(ids);
            return Ok(result);
        }
        catch (Exception)
        {
            return ServerFailure();
        }
    }

    private static ProblemDetails Problem(int status, string title) => new()
    {
        Status = status,
        Title = title
    };

    private ObjectResult ServerFailure() =>
        StatusCode(500, Problem(500, "The server operation could not be completed."));
}

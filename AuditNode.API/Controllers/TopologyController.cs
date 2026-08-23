using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.API.Security;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
public class TopologyController : ControllerBase
{
    private readonly ITopologyRepository _topologyRepository;

    public TopologyController(ITopologyRepository topologyRepository)
    {
        _topologyRepository = topologyRepository;
    }

    [HttpGet("tree")]
    public async Task<ActionResult<IEnumerable<TopologyTreeDto>>> GetTree(
        [FromQuery] Guid? datacenterId,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null,
        [FromQuery] List<string>? labels = null)
    {
        var pageSize = take ?? 100;
        if (skip < 0 || pageSize <= 0)
            return BadRequest(Problem(400, "Skip must be non-negative and take must be positive."));
        pageSize = Math.Min(pageSize, 100);

        try
        {
            return Ok(await _topologyRepository.GetTopologyTreeAsync(datacenterId, skip, pageSize, labels));
        }
        catch (Exception)
        {
            return Failure("Topology tree could not be retrieved.");
        }
    }

    [HttpGet("map")]
    public async Task<ActionResult<DependencyMapDto>> GetDependencyMap(
        [FromQuery] string? environment,
        [FromQuery] Guid? datacenterId,
        [FromQuery] List<string>? labels = null)
    {
        try
        {
            return Ok(await _topologyRepository.GetDependencyMapAsync(environment, datacenterId, labels));
        }
        catch (Exception)
        {
            return Failure("Dependency map could not be retrieved.");
        }
    }

    [HttpGet("status")]
    public async Task<ActionResult<IEnumerable<ApplicationStatusDto>>> GetStatus()
    {
        try
        {
            return Ok(await _topologyRepository.GetApplicationStatusAsync());
        }
        catch (Exception)
        {
            return Failure("Application status could not be retrieved.");
        }
    }

    [HttpGet("state")]
    public async Task<ActionResult<TopologyStateDto>> GetState()
    {
        try
        {
            return Ok(await _topologyRepository.GetTopologyStateAsync());
        }
        catch (Exception)
        {
            return Failure("Topology state could not be retrieved.");
        }
    }

    [HttpPost("state")]
    [HttpPut("state")]
    [WorkspaceMutation(ownerOrAdminOnly: true)]
    public async Task<IActionResult> SaveState([FromBody] TopologyStateDto state)
    {
        if (state?.Nodes is null || state.Edges is null)
            return BadRequest(Problem(400, "A complete topology state is required."));

        try
        {
            var status = await _topologyRepository.SaveTopologyStateAsync(state);
            return status switch
            {
                TopologyStateStatus.Success => NoContent(),
                TopologyStateStatus.DuplicateId => Conflict(Problem(409, "Topology node and edge IDs must be unique.")),
                TopologyStateStatus.InvalidParent => BadRequest(Problem(400, "Topology parent relationships are invalid.")),
                TopologyStateStatus.InvalidReference => BadRequest(Problem(400, "Topology references are invalid for the current workspace.")),
                TopologyStateStatus.InvalidEdge => BadRequest(Problem(400, "Topology edges are invalid.")),
                _ => BadRequest(Problem(400, "Topology state is invalid."))
            };
        }
        catch (Exception)
        {
            return Failure("Topology state could not be saved.");
        }
    }

    private static ProblemDetails Problem(int status, string title) => new() { Status = status, Title = title };
    private ObjectResult Failure(string title) => StatusCode(500, Problem(500, title));
}

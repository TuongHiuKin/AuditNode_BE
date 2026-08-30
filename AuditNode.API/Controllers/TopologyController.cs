using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.API.Security;
using AuditNode.API.Middleware;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
public class TopologyController : ControllerBase
{
    private readonly ITopologyRepository _topologyRepository;
    private readonly ITopologyCommandService _topologyCommandService;

    public TopologyController(ITopologyRepository topologyRepository, ITopologyCommandService topologyCommandService)
    {
        _topologyRepository = topologyRepository;
        _topologyCommandService = topologyCommandService;
    }

    [HttpGet("tree")]
    [SkipWorkspaceValidation]
    public async Task<ActionResult<IEnumerable<TopologyTreeDto>>> GetTree(
        [FromQuery] Guid? datacenterId,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null,
        [FromQuery] List<string>? labels = null,
        [FromQuery] string? ownerUserId = null)
    {
        var pageSize = take ?? 100;
        if (skip < 0 || pageSize <= 0)
            return BadRequest(Problem(400, "Skip must be non-negative and take must be positive."));
        pageSize = Math.Min(pageSize, 100);

        try
        {
            return Ok(await _topologyRepository.GetTopologyTreeAsync(datacenterId, skip, pageSize, labels, ownerUserId));
        }
        catch (Exception)
        {
            return Failure("Topology tree could not be retrieved.");
        }
    }

    [HttpGet("map")]
    [SkipWorkspaceValidation]
    public async Task<ActionResult<DependencyMapDto>> GetDependencyMap(
        [FromQuery] string? environment,
        [FromQuery] Guid? datacenterId,
        [FromQuery] List<string>? labels = null,
        [FromQuery] string? ownerUserId = null)
    {
        try
        {
            return Ok(await _topologyRepository.GetDependencyMapAsync(environment, datacenterId, labels, ownerUserId));
        }
        catch (Exception)
        {
            return Failure("Dependency map could not be retrieved.");
        }
    }

    [HttpGet("status")]
    [SkipWorkspaceValidation]
    public async Task<ActionResult<IEnumerable<ApplicationStatusDto>>> GetStatus([FromQuery] string? ownerUserId = null)
    {
        try
        {
            return Ok(await _topologyRepository.GetApplicationStatusAsync(ownerUserId));
        }
        catch (Exception)
        {
            return Failure("Application status could not be retrieved.");
        }
    }

    [HttpGet("state")]
    [SkipWorkspaceValidation]
    public async Task<ActionResult<TopologyStateDto>> GetState([FromQuery] string? ownerUserId = null)
    {
        try
        {
            return Ok(await _topologyRepository.GetTopologyStateAsync(ownerUserId));
        }
        catch (Exception)
        {
            return Failure("Topology state could not be retrieved.");
        }
    }

    [HttpPost("state")]
    [HttpPut("state")]
    [SkipWorkspaceValidation]
    public async Task<IActionResult> SaveState([FromBody] SaveTopologyStateDto state)
    {
        if (state?.Version is null || state.Nodes is null || state.Edges is null || state.Dependencies is null)
            return BadRequest(Problem(400, "Version, nodes, edges, and dependencies are required."));

        try
        {
            var status = await _topologyRepository.SaveTopologyStateAsync(state);
            return status switch
            {
                TopologyStateStatus.Success => NoContent(),
                TopologyStateStatus.DuplicateId => Conflict(Problem(409, "Topology node and edge IDs must be unique.")),
                TopologyStateStatus.InvalidParent => BadRequest(Problem(400, "Topology parent relationships are invalid.")),
                TopologyStateStatus.InvalidReference => BadRequest(Problem(400, "Topology references are invalid for the owner catalog.")),
                TopologyStateStatus.InvalidEdge => BadRequest(Problem(400, "Topology edges are invalid.")),
                TopologyStateStatus.InvalidDependency => BadRequest(Problem(400, "Topology dependencies are invalid.")),
                TopologyStateStatus.Forbidden => StatusCode(403, Problem(403, "Topology replacement is forbidden.")),
                TopologyStateStatus.Conflict => Conflict(Problem(409, "The topology changed. Refresh and retry.")),
                _ => BadRequest(Problem(400, "Topology state is invalid."))
            };
        }
        catch (Exception)
        {
            return Failure("Topology state could not be saved.");
        }
    }

    [HttpPost("commands")]
    [SkipWorkspaceValidation]
    public async Task<IActionResult> ExecuteCommands([FromBody] TopologyCommandBatchDto batch, CancellationToken cancellationToken)
    {
        if (batch is null || batch.Operations is null)
            return BadRequest(Problem(400, "A topology command batch is required."));

        try
        {
            var result = await _topologyCommandService.ExecuteAsync(batch, cancellationToken);
            return result.Status switch
            {
                TopologyCommandStatus.Success => Ok(new { version = result.Version }),
                TopologyCommandStatus.Conflict => Conflict(Problem(409, result.Error ?? "The topology changed. Refresh and retry.")),
                TopologyCommandStatus.Forbidden => StatusCode(403, Problem(403, "A topology resource is outside the granted scope.")),
                _ => BadRequest(Problem(400, result.Error ?? "The topology command batch is invalid."))
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure("Topology commands could not be applied.");
        }
    }

    private static ProblemDetails Problem(int status, string title) => new() { Status = status, Title = title };
    private ObjectResult Failure(string title) => StatusCode(500, Problem(500, title));
}

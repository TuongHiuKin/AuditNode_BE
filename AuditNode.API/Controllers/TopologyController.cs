using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
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
        [FromQuery] int? take = null)
    {
        int pageSize = take ?? 100;
        if (pageSize > 100) pageSize = 100; // Hard limit for protection

        var tree = await _topologyRepository.GetTopologyTreeAsync(datacenterId, skip, pageSize);
        return Ok(tree);
    }

    [HttpGet("map")]
    public async Task<ActionResult<DependencyMapDto>> GetDependencyMap(
        [FromQuery] string? environment,
        [FromQuery] Guid? datacenterId)
    {
        var map = await _topologyRepository.GetDependencyMapAsync(environment, datacenterId);
        return Ok(map);
    }

    [HttpGet("status")]
    public async Task<ActionResult<IEnumerable<ApplicationStatusDto>>> GetStatus()
    {
        var status = await _topologyRepository.GetApplicationStatusAsync();
        return Ok(status);
    }

    [HttpPost("state")]
    public async Task<IActionResult> SaveState([FromBody] SaveTopologyStateDto state)
    {
        if (state == null || state.Nodes == null)
        {
            return BadRequest("Invalid topology state payload.");
        }

        try
        {
            await _topologyRepository.SaveTopologyStateAsync(state);
            return Ok(new { message = "Topology state saved successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

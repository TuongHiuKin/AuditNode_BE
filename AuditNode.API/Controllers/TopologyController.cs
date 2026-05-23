using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

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
    public async Task<ActionResult<DependencyMapDto>> GetDependencyMap()
    {
        var map = await _topologyRepository.GetDependencyMapAsync();
        return Ok(map);
    }
}

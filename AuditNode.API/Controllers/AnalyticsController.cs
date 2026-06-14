using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsRepository _analyticsRepository;

    public AnalyticsController(IAnalyticsRepository analyticsRepository)
    {
        _analyticsRepository = analyticsRepository;
    }

    [HttpGet("topology")]
    public async Task<ActionResult> GetTopology([FromQuery] string? environment, [FromQuery] Guid? datacenterId)
    {
        try
        {
            var topologyData = await _analyticsRepository.GetTopologyAsync(environment, datacenterId);
            return Ok(topologyData);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("dependencies")]
    public async Task<ActionResult> GetDependencies([FromQuery] string? environment, [FromQuery] Guid? datacenterId)
    {
        try
        {
            var dependencyData = await _analyticsRepository.GetDependenciesAsync(environment, datacenterId);
            return Ok(dependencyData);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

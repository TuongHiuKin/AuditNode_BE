using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.API.Errors;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
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
        catch (Exception)
        {
            return StatusCode(500, ApiProblem.Create(ControllerContext.HttpContext, 500, "Topology analytics could not be retrieved."));
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
        catch (Exception)
        {
            return StatusCode(500, ApiProblem.Create(ControllerContext.HttpContext, 500, "Dependency analytics could not be retrieved."));
        }
    }
}

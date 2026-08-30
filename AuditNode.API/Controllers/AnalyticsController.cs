using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.API.Errors;
using AuditNode.API.Middleware;
using AuditNode.Application.DTOs;
using AuditNode.Application.Exceptions;

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

    [SkipWorkspaceValidation]
    [HttpGet("topology")]
    public async Task<ActionResult> GetTopology([FromQuery] string? environment, [FromQuery] Guid? datacenterId, [FromQuery] string? view = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var topologyData = await _analyticsRepository.GetTopologyCatalogAsync(CatalogPageQuery.Parse(view, 25, null).View, environment, datacenterId, cancellationToken);
            return Ok(topologyData);
        }
        catch (CatalogQueryValidationException exception)
        {
            return BadRequest(ApiProblem.Create(ControllerContext.HttpContext, 400, exception.Message));
        }
        catch (Exception)
        {
            return StatusCode(500, ApiProblem.Create(ControllerContext.HttpContext, 500, "Topology analytics could not be retrieved."));
        }
    }

    [SkipWorkspaceValidation]
    [HttpGet("dependencies")]
    public async Task<ActionResult> GetDependencies([FromQuery] string? environment, [FromQuery] Guid? datacenterId, [FromQuery] string? view = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var dependencyData = await _analyticsRepository.GetDependenciesCatalogAsync(CatalogPageQuery.Parse(view, 25, null).View, environment, datacenterId, cancellationToken);
            return Ok(dependencyData);
        }
        catch (CatalogQueryValidationException exception)
        {
            return BadRequest(ApiProblem.Create(ControllerContext.HttpContext, 400, exception.Message));
        }
        catch (Exception)
        {
            return StatusCode(500, ApiProblem.Create(ControllerContext.HttpContext, 500, "Dependency analytics could not be retrieved."));
        }
    }
}

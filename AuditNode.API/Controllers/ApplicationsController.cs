using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route(ApiRoutes.BaseRoute)]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationService _applicationService;

    public ApplicationsController(IApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApplicationResponseDto>>> GetApplications(
        [FromQuery] string? labelKey = null,
        [FromQuery] string? labelValue = null)
    {
        try
        {
            return Ok(await _applicationService.GetAllAsync(labelKey, labelValue));
        }
        catch (Exception)
        {
            return Failure(500, "Applications could not be retrieved.");
        }
    }

    [HttpGet("export")]
    public async Task<ActionResult<IEnumerable<ApplicationResponseDto>>> ExportApplications([FromQuery] List<Guid> ids)
    {
        if (ids is null || ids.Count == 0 || ids.Any(id => id == Guid.Empty))
            return BadRequest(Problem(400, "Non-empty application identifiers are required."));

        try
        {
            return Ok(await _applicationService.GetByIdsAsync(ids));
        }
        catch (Exception)
        {
            return Failure(500, "Applications could not be exported.");
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApplicationResponseDto>> GetApplication(Guid id)
    {
        if (id == Guid.Empty)
            return BadRequest(Problem(400, "A non-empty application identifier is required."));

        try
        {
            var result = await _applicationService.GetByIdAsync(id);
            return result is null
                ? NotFound(Problem(404, "Application was not found."))
                : Ok(result);
        }
        catch (Exception)
        {
            return Failure(500, "Application could not be retrieved.");
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApplicationResponseDto>> PostApplication([FromBody] CreateApplicationDto appDto)
    {
        try
        {
            var result = await _applicationService.CreateAsync(appDto);
            return result.Status switch
            {
                ApplicationOperationStatus.Success when result.Application is not null =>
                    CreatedAtAction(nameof(GetApplication), new { id = result.Application.Id }, result.Application),
                ApplicationOperationStatus.DuplicateAppCode =>
                    Conflict(Problem(409, "An application with this code already exists in the current workspace.")),
                ApplicationOperationStatus.ServerNotFound =>
                    BadRequest(Problem(400, "Deployment server was not found in the current workspace.")),
                ApplicationOperationStatus.PortCollision =>
                    Conflict(Problem(409, "The target server port is already assigned.")),
                ApplicationOperationStatus.InvalidRequest or ApplicationOperationStatus.InvalidWorkspace =>
                    BadRequest(Problem(400, "Application data is invalid for the current workspace.")),
                ApplicationOperationStatus.Forbidden => Forbid(),
                _ => Failure(500, "Application could not be created.")
            };
        }
        catch (Exception)
        {
            return Failure(500, "Application could not be created.");
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> PutApplication(Guid id, [FromBody] UpdateApplicationDto updateDto)
    {
        if (id == Guid.Empty)
            return BadRequest(Problem(400, "A non-empty application identifier is required."));

        try
        {
            var result = await _applicationService.UpdateAsync(id, updateDto);
            return result.Status switch
            {
                ApplicationOperationStatus.Success => NoContent(),
                ApplicationOperationStatus.NotFound => NotFound(Problem(404, "Application was not found.")),
                ApplicationOperationStatus.DeploymentNotFound =>
                    NotFound(Problem(404, "Deployment was not found for this application.")),
                ApplicationOperationStatus.ServerNotFound =>
                    BadRequest(Problem(400, "Deployment server was not found in the current workspace.")),
                ApplicationOperationStatus.PortCollision =>
                    Conflict(Problem(409, "The target server port is already assigned.")),
                ApplicationOperationStatus.InvalidRequest or ApplicationOperationStatus.InvalidWorkspace =>
                    BadRequest(Problem(400, "Application data is invalid for the current workspace.")),
                ApplicationOperationStatus.Forbidden => Forbid(),
                _ => Failure(500, "Application could not be updated.")
            };
        }
        catch (Exception)
        {
            return Failure(500, "Application could not be updated.");
        }
    }

    private static ProblemDetails Problem(int status, string title) => new() { Status = status, Title = title };
    private ObjectResult Failure(int status, string title) => StatusCode(status, Problem(status, title));
}

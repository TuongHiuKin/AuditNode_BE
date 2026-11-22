using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.Application.Exceptions;

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
    [ProducesResponseType(typeof(CursorPageDto<ApplicationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CursorPageDto<ApplicationResponseDto>>> GetApplications(
        [FromQuery] string? labelKey = null,
        [FromQuery] string? labelValue = null,
        [FromQuery] string? ownerUserId = null,
        [FromQuery] string? view = null,
        [FromQuery] int? limit = null,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = CatalogPageQuery.Parse(view, limit, cursor);
            return Ok(await _applicationService.GetCatalogPageAsync(query, labelKey, labelValue, ownerUserId, cancellationToken));
        }
        catch (CatalogQueryValidationException exception)
        {
            return BadRequest(Problem(400, exception.Message));
        }
        catch (Exception)
        {
            return Failure(500, "Applications could not be retrieved.");
        }
    }

    [HttpGet("export")]
    public async Task<ActionResult<IEnumerable<ApplicationResponseDto>>> ExportApplications(
        [FromQuery] List<Guid> ids,
        [FromQuery] string? view = null,
        CancellationToken cancellationToken = default)
    {
        if (ids is null || ids.Count == 0 || ids.Any(id => id == Guid.Empty))
            return BadRequest(Problem(400, "Non-empty application identifiers are required."));

        try
        {
            return Ok(await _applicationService.ExportCatalogAsync(ids, CatalogPageQuery.Parse(view, 25, null).View, cancellationToken));
        }
        catch (CatalogQueryValidationException exception)
        {
            return BadRequest(Problem(400, exception.Message));
        }
        catch (Exception)
        {
            return Failure(500, "Applications could not be exported.");
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApplicationResponseDto>> GetApplication(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            return BadRequest(Problem(400, "A non-empty application identifier is required."));

        try
        {
            var result = await _applicationService.GetCatalogDetailAsync(id, cancellationToken);
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
                    Conflict(Problem(409, "An application with this code already exists in your catalog.")),
                ApplicationOperationStatus.ServerNotFound =>
                    BadRequest(Problem(400, "Deployment server was not found in your catalog.")),
                ApplicationOperationStatus.PortCollision =>
                    Conflict(Problem(409, "The target server port is already assigned.")),
                ApplicationOperationStatus.InvalidRequest =>
                    BadRequest(Problem(400, "Application data is invalid for your catalog.")),
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
                    BadRequest(Problem(400, "Deployment server was not found in the resource owner's catalog.")),
                ApplicationOperationStatus.PortCollision =>
                    Conflict(Problem(409, "The target server port is already assigned.")),
                ApplicationOperationStatus.InvalidRequest =>
                    BadRequest(Problem(400, "Application data is invalid for the resource owner's catalog.")),
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

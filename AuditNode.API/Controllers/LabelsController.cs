using AuditNode.Application.DTOs;
using AuditNode.Application.Exceptions;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/labels")]
[Route("api/v1/inventory/labels")]
public sealed class LabelsController(ILabelCatalogService labels) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CursorPageDto<CatalogLabelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CursorPageDto<CatalogLabelDto>>> GetLabels(
        [FromQuery] string? view = null,
        [FromQuery] int? limit = null,
        [FromQuery] string? cursor = null,
        [FromQuery] string? ownerUserId = null,
        [FromQuery] string? labelKey = null,
        [FromQuery] string? labelValue = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await labels.GetLabelsAsync(
                CatalogPageQuery.Parse(view, limit, cursor), ownerUserId, labelKey, labelValue, cancellationToken));
        }
        catch (CatalogQueryValidationException exception)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = exception.Message });
        }
    }
}

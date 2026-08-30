using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.API.Errors;
using AuditNode.API.Middleware;
using AuditNode.Application.Exceptions;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/search")]
public class InventorySearchController : ControllerBase
{
    private readonly IInventorySearchService _searchService;

    public InventorySearchController(IInventorySearchService searchService)
    {
        _searchService = searchService;
    }

    [SkipWorkspaceValidation]
    [HttpGet]
    [ProducesResponseType(typeof(CursorPageDto<SearchResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CursorPageDto<SearchResultDto>>> Search(
        [FromQuery] string? q = null,
        [FromQuery] string? view = null,
        [FromQuery] int? limit = null,
        [FromQuery] string? cursor = null,
        [FromQuery(Name = "keyword")] string? legacyKeyword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _searchService.SearchAsync(q ?? legacyKeyword ?? string.Empty, CatalogPageQuery.Parse(view, limit, cursor), cancellationToken);
            return Ok(results);
        }
        catch (CatalogQueryValidationException exception)
        {
            return BadRequest(ApiProblem.Create(ControllerContext.HttpContext, 400, exception.Message));
        }
        catch (Exception)
        {
            return StatusCode(500, ApiProblem.Create(
                ControllerContext.HttpContext,
                500,
                "Inventory search could not be completed."));
        }
    }
}

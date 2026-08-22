using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.API.Errors;

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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SearchResultDto>>> Search([FromQuery] string? keyword)
    {
        try
        {
            var results = await _searchService.SearchAsync(keyword ?? string.Empty);
            return Ok(results);
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

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/search")]
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
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

using AuditNode.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/inventory/labels")]
public class LabelsController : ControllerBase
{
    private readonly AuditDbContext _context;

    public LabelsController(AuditDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// GET /api/v1/inventory/labels
    /// Retrieves all unique labels for the authenticated user to populate frontend dropdowns.
    /// </summary>
    [HttpGet("/api/v1/inventory/labels")]
    public async Task<IActionResult> GetLabels()
    {
        // 1. Extract OwnerId from Claims
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("User ID not found in token.");
        }

        // 2. Fetch and Project Labels directly from the Database
        var labels = await _context.Labels
            .AsNoTracking() // Architectural Best Practice: Always use AsNoTracking for read-only GET requests
            .Where(l => l.OwnerId == currentUserId)
            .OrderBy(l => l.Key)
            .ThenBy(l => l.Value)
            .Select(l => new 
            {
                l.Id,
                l.Key,
                l.Value,
                l.ColorHex
            })
            .ToListAsync();

        return Ok(labels);
    }
}

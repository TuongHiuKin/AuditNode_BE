using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;

namespace AuditNode.API.Controllers;

public record CreateFrameDto(string Name, double XPosition, double YPosition, double Width, double Height);
public record UpdateFrameDto(string Name, double XPosition, double YPosition, double Width, double Height);
public record AssignNodeDto(Guid EntityId, string EntityType); // "server" or "app"

[Authorize]
[ApiController]
[Route("api/v1/frames")]
public class BoundaryFramesController : ControllerBase
{
    private readonly AuditDbContext _context;

    public BoundaryFramesController(AuditDbContext context)
    {
        _context = context;
    }

    private string? GetCurrentUserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<IActionResult> GetFrames()
    {
        var userIdString = GetCurrentUserId();
        if (!Guid.TryParse(userIdString, out var parsedOwnerId)) return Unauthorized("Invalid User ID in token.");

        var frames = await _context.BoundaryFrames
            .AsNoTracking()
            .Where(f => f.OwnerId == parsedOwnerId)
            .Select(f => new 
            {
                f.Id, f.Name, f.XPosition, f.YPosition, f.Width, f.Height, f.CreatedAt
            })
            .ToListAsync();

        return Ok(frames);
    }

    [HttpPost]
    public async Task<IActionResult> CreateFrame([FromBody] CreateFrameDto dto)
    {
        var userIdString = GetCurrentUserId();
        if (!Guid.TryParse(userIdString, out var parsedOwnerId)) return Unauthorized("Invalid User ID in token.");

        var frame = new BoundaryFrame
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            XPosition = dto.XPosition,
            YPosition = dto.YPosition,
            Width = dto.Width,
            Height = dto.Height,
            OwnerId = parsedOwnerId,
            CreatedAt = DateTime.UtcNow
        };

        _context.BoundaryFrames.Add(frame);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetFrames), new { id = frame.Id }, frame);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateFrame(Guid id, [FromBody] UpdateFrameDto dto)
    {
        var userIdString = GetCurrentUserId();
        if (!Guid.TryParse(userIdString, out var parsedOwnerId)) return Unauthorized("Invalid User ID in token.");

        var frame = await _context.BoundaryFrames.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == parsedOwnerId);
        if (frame == null) return NotFound("Frame not found.");

        frame.Name = dto.Name;
        frame.XPosition = dto.XPosition;
        frame.YPosition = dto.YPosition;
        frame.Width = dto.Width;
        frame.Height = dto.Height;

        await _context.SaveChangesAsync();
        return Ok(frame);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFrame(Guid id)
    {
        var userIdString = GetCurrentUserId();
        if (!Guid.TryParse(userIdString, out var parsedOwnerId)) return Unauthorized("Invalid User ID in token.");

        var frame = await _context.BoundaryFrames.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == parsedOwnerId);
        if (frame == null) return NotFound("Frame not found.");

        _context.BoundaryFrames.Remove(frame);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{frameId}/assign")]
    public async Task<IActionResult> AssignNode(Guid frameId, [FromBody] AssignNodeDto dto)
    {
        var userIdString = GetCurrentUserId();
        if (!Guid.TryParse(userIdString, out var parsedOwnerId)) return Unauthorized("Invalid User ID in token.");
        var userId = userIdString; // Keep original string for Server/App queries

        var frameExists = await _context.BoundaryFrames.AnyAsync(f => f.Id == frameId && f.OwnerId == parsedOwnerId);
        if (!frameExists) return NotFound("Frame not found or unauthorized.");

        if (dto.EntityType.Equals("server", StringComparison.OrdinalIgnoreCase))
        {
            var server = await _context.Servers.FirstOrDefaultAsync(s => s.Id == dto.EntityId && s.OwnerId == userId);
            if (server == null) return NotFound("Server not found or unauthorized.");
            
            server.ParentFrameId = frameId;
        }
        else if (dto.EntityType.Equals("app", StringComparison.OrdinalIgnoreCase))
        {
            var app = await _context.Applications.FirstOrDefaultAsync(a => a.Id == dto.EntityId && a.OwnerId == userId);
            if (app == null) return NotFound("Application not found or unauthorized.");
            
            app.ParentFrameId = frameId;
        }
        else
        {
            return BadRequest("Invalid EntityType. Must be 'server' or 'app'.");
        }

        await _context.SaveChangesAsync();
        return Ok(new { Message = "Node assigned to frame successfully." });
    }
}

using AuditNode.Application.DTOs;
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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<LabelDto>>> GetLabels()
    {
        var labels = await _context.Labels
            .Select(l => new LabelDto
            {
                Key = l.Key,
                Value = l.Value
            })
            .Distinct()
            .ToListAsync();

        return Ok(labels);
    }
}

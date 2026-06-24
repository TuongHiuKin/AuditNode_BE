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
    public async Task<ActionResult<IEnumerable<ApplicationResponseDto>>> GetApplications([FromQuery] string[]? labels)
    {
        var result = await _applicationService.GetAllAsync(labels);
        return Ok(result);
    }

    [HttpGet("export")]
    public async Task<ActionResult<IEnumerable<ApplicationResponseDto>>> ExportApplications([FromQuery] List<Guid> ids)
    {
        if (ids == null || ids.Count == 0)
        {
            return BadRequest(new { error = "No IDs provided for export." });
        }

        var result = await _applicationService.GetByIdsAsync(ids);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApplicationResponseDto>> GetApplication(Guid id)
    {
        var result = await _applicationService.GetByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ApplicationResponseDto>> PostApplication([FromBody] CreateApplicationDto appDto)
    {
        var result = await _applicationService.CreateAsync(appDto);
        return CreatedAtAction(nameof(GetApplication), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> PutApplication(Guid id, [FromBody] UpdateApplicationDto updateDto)
    {
        var result = await _applicationService.UpdateAsync(id, updateDto);
        if (!result) return NotFound();
        return NoContent();
    }
}

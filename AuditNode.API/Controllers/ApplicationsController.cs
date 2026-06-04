using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace AuditNode.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationService _applicationService;

    public ApplicationsController(IApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApplicationResponseDto>>> GetApplications()
    {
        try
        {
            var applications = await _applicationService.GetAllAsync();
            return Ok(applications);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApplicationResponseDto>> GetApplication(Guid id)
    {
        var application = await _applicationService.GetByIdAsync(id);
        if (application == null)
        {
            return NotFound();
        }
        return Ok(application);
    }

    [HttpPost]
    public async Task<ActionResult> PostApplication([FromBody] CreateApplicationDto appDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var responseDto = await _applicationService.CreateAsync(appDto);
            return CreatedAtAction(nameof(GetApplication), new { id = responseDto.Id }, responseDto);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, details = ex.InnerException?.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutApplication(Guid id, [FromBody] UpdateApplicationDto updateDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _applicationService.UpdateAsync(id, updateDto);
            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

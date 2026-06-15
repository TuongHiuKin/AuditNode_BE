using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationService _applicationService;
    private readonly ILogger<ApplicationsController> _logger;

    public ApplicationsController(IApplicationService applicationService, ILogger<ApplicationsController> logger)
    {
        _applicationService = applicationService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApplicationResponseDto>>> GetApplications()
    {
        // Example: Extracting user context from HttpContext.User
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _logger.LogInformation("Applications accessed by user: {UserId}", userId);

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

    [HttpGet("export")]
    public async Task<ActionResult<IEnumerable<ApplicationResponseDto>>> ExportApplications([FromQuery] Guid[] ids)
    {
        try
        {
            if (ids == null || ids.Length == 0)
            {
                return BadRequest(new { error = "No IDs provided for export." });
            }

            var applications = await _applicationService.GetByIdsAsync(ids);
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
        _logger.LogInformation("=== HTTP PUT APPLICATION TRACE ===");
        _logger.LogInformation("Incoming App ID: {Id}", id);
        _logger.LogInformation("Incoming Server ID: {ServerId}", updateDto.TargetServerId);
        _logger.LogInformation("Incoming Port Number: {Port}", updateDto.PortNumber);
        _logger.LogInformation("==================================");

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

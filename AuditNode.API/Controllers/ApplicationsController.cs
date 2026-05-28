using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationRepository _applicationRepository;

    public ApplicationsController(IApplicationRepository applicationRepository)
    {
        _applicationRepository = applicationRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApplicationResponseDto>>> GetApplications()
    {
        try
        {
            var applications = await _applicationRepository.GetApplicationsAsync();
            return Ok(applications);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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
            if (string.IsNullOrWhiteSpace(appDto.AppCode) ||
                string.IsNullOrWhiteSpace(appDto.AppName) ||
                string.IsNullOrWhiteSpace(appDto.OwnerTeam) ||
                appDto.ServerId == Guid.Empty)
            {
                return BadRequest(new { error = "Required fields are missing or invalid" });
            }

            var appId = Guid.NewGuid();
            var application = new AppEntity
            {
                Id = appId,
                AppCode = appDto.AppCode.ToUpper(),
                AppName = appDto.AppName,
                OwnerTeam = appDto.OwnerTeam,
                Risk = string.IsNullOrWhiteSpace(appDto.Risk) ? "LOW" : appDto.Risk,
                Icon = appDto.Icon ?? string.Empty,
                TechStack = appDto.TechStack ?? string.Empty,
                ServerId = appDto.ServerId
            };

            var portMapping = new PortMapping
            {
                Id = Guid.NewGuid(),
                AppId = appId,
                ServerId = appDto.ServerId,
                PortNumber = appDto.PortNumber,
                Protocol = appDto.Protocol
            };

            application.PortMappings.Add(portMapping);

            var registeredApp = await _applicationRepository.RegisterApplicationAsync(application);

            var responseDto = new ApplicationResponseDto
            {
                Id = registeredApp.Id,
                AppCode = registeredApp.AppCode,
                AppName = registeredApp.AppName,
                OwnerTeam = registeredApp.OwnerTeam,
                Risk = registeredApp.Risk,
                Icon = registeredApp.Icon,
                TechStack = registeredApp.TechStack,
                Servers = new List<ServerOnApplicationDto>
                {
                    new ServerOnApplicationDto
                    {
                        Id = appDto.ServerId,
                        PortNumber = appDto.PortNumber,
                        Protocol = appDto.Protocol
                    }
                }
            };

            return CreatedAtAction(nameof(GetApplications), new { id = registeredApp.Id }, responseDto);
        }
        catch (Exception ex)
        {
            // Log full exception details if possible, or return detailed message for debugging
            return StatusCode(500, new { error = ex.Message, details = ex.InnerException?.Message });
        }
    }
}

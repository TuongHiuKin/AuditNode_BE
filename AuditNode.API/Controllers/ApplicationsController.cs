using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
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
        try
        {
            if (string.IsNullOrWhiteSpace(appDto.AppCode) ||
                string.IsNullOrWhiteSpace(appDto.AppName) ||
                appDto.OwnerId == Guid.Empty)
            {
                return BadRequest(new { error = "Required fields are missing" });
            }

            var application = new AppEntity
            {
                Id = Guid.NewGuid(),
                AppCode = appDto.AppCode.ToUpper(),
                AppName = appDto.AppName,
                OwnerId = appDto.OwnerId,
                PortNumber = appDto.PortNumber,
                Protocol = appDto.Protocol,
                Risk = appDto.Risk,
                Icon = appDto.Icon,
                TechStack = appDto.TechStack,
                RiskLevel = appDto.RiskLevel,
                TargetApplicationId = appDto.TargetApplicationId,
                ServerId = appDto.ServerId
            };

            await _applicationRepository.CreateApplicationAsync(application);

            return CreatedAtAction(nameof(GetApplications), new { id = application.Id }, application);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Application.Services;

public class ApplicationService : IApplicationService
{
    private readonly IApplicationRepository _repository;

    public ApplicationService(IApplicationRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<ApplicationResponseDto>> GetAllAsync()
    {
        return await _repository.GetApplicationsAsync();
    }

    public async Task<ApplicationResponseDto?> GetByIdAsync(Guid id)
    {
        var app = await _repository.GetByIdAsync(id);
        if (app == null) return null;

        return new ApplicationResponseDto
        {
            Id = app.Id,
            AppCode = app.AppCode,
            AppName = app.AppName,
            OwnerTeam = app.OwnerTeam,
            Risk = app.Risk,
            Icon = app.Icon,
            TechStack = app.TechStack,
            Servers = app.PortMappings.Select(pm => new ServerOnApplicationDto
            {
                Id = pm.ServerId,
                Hostname = pm.Server?.Hostname ?? string.Empty,
                IpAddress = pm.Server?.IpAddress ?? string.Empty,
                PortNumber = pm.PortNumber,
                Protocol = pm.Protocol
            }).ToList()
        };
    }

    public async Task<ApplicationResponseDto> CreateAsync(CreateApplicationDto appDto)
    {
        var appId = Guid.NewGuid();
        var application = new AppEntity
        {
            Id = appId,
            AppCode = appDto.AppCode.ToUpper(),
            AppName = appDto.AppName,
            OwnerTeam = appDto.OwnerTeam,
            Risk = string.IsNullOrWhiteSpace(appDto.Risk) ? "LOW" : appDto.Risk,
            Icon = appDto.Icon ?? string.Empty,
            TechStack = appDto.TechStack ?? string.Empty
        };

        var registeredApp = await _repository.RegisterApplicationAsync(application);

        return new ApplicationResponseDto
        {
            Id = registeredApp.Id,
            AppCode = registeredApp.AppCode,
            AppName = registeredApp.AppName,
            OwnerTeam = registeredApp.OwnerTeam,
            Risk = registeredApp.Risk,
            Icon = registeredApp.Icon,
            TechStack = registeredApp.TechStack,
            Servers = new List<ServerOnApplicationDto>()
        };
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateApplicationDto updateDto)
    {
        var existingApp = await _repository.GetByIdAsync(id);
        if (existingApp == null) return false;

        existingApp.AppName = updateDto.AppName;
        existingApp.OwnerTeam = updateDto.OwnerTeam;
        existingApp.Risk = updateDto.Risk;
        existingApp.Icon = updateDto.Icon;
        existingApp.TechStack = updateDto.TechStack;

        await _repository.UpdateAsync(existingApp);
        return true;
    }
}

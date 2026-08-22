using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Infrastructure.Services;

public class ApplicationService : IApplicationService
{
    private readonly IApplicationRepository _repository;
    private readonly ITenantProvider _tenantProvider;

    public ApplicationService(IApplicationRepository repository, ITenantProvider tenantProvider)
    {
        _repository = repository;
        _tenantProvider = tenantProvider;
    }

    public Task<IEnumerable<ApplicationResponseDto>> GetAllAsync(string? labelKey = null, string? labelValue = null) =>
        HasWorkspace()
            ? _repository.GetApplicationsAsync(labelKey, labelValue)
            : Task.FromResult<IEnumerable<ApplicationResponseDto>>(Array.Empty<ApplicationResponseDto>());

    public Task<IEnumerable<ApplicationResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids) =>
        HasWorkspace()
            ? _repository.GetByIdsAsync(ids)
            : Task.FromResult<IEnumerable<ApplicationResponseDto>>(Array.Empty<ApplicationResponseDto>());

    public async Task<ApplicationResponseDto?> GetByIdAsync(Guid id)
    {
        if (!HasWorkspace() || id == Guid.Empty)
            return null;

        var application = await _repository.GetByIdAsync(id);
        return application is null ? null : Map(application);
    }

    public async Task<ApplicationOperationResult> CreateAsync(CreateApplicationDto createDto)
    {
        if (!HasWorkspace())
            return new(ApplicationOperationStatus.InvalidWorkspace);

        var appCode = createDto.AppCode.Trim().ToUpperInvariant();
        if (await _repository.AppCodeExistsAsync(appCode))
            return new(ApplicationOperationStatus.DuplicateAppCode);

        PortMapping? deployment = null;
        if (createDto.Deployment is not null)
        {
            var validation = await ValidateDeploymentAsync(
                createDto.Deployment.ServerId,
                createDto.Deployment.PortNumber,
                null);
            if (validation != ApplicationOperationStatus.Success)
                return new(validation);

            deployment = new PortMapping
            {
                Id = Guid.NewGuid(),
                ServerId = createDto.Deployment.ServerId,
                PortNumber = createDto.Deployment.PortNumber,
                Protocol = createDto.Deployment.Protocol.Trim().ToUpperInvariant()
            };
        }

        var application = new AppEntity
        {
            Id = Guid.NewGuid(),
            AppCode = appCode,
            AppName = createDto.AppName,
            OwnerTeam = createDto.OwnerTeam,
            Risk = string.IsNullOrWhiteSpace(createDto.Risk) ? "LOW" : createDto.Risk,
            Icon = createDto.Icon ?? string.Empty,
            TechStack = createDto.TechStack ?? string.Empty
        };
        if (deployment is not null)
            deployment.AppId = application.Id;

        try
        {
            await _repository.CreateAsync(application, createDto.Labels, deployment);
            var stored = await _repository.GetByIdAsync(application.Id);
            return new(ApplicationOperationStatus.Success, Map(stored ?? application));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return new(deployment is null
                ? ApplicationOperationStatus.DuplicateAppCode
                : ApplicationOperationStatus.PortCollision);
        }
    }

    public async Task<ApplicationOperationResult> UpdateAsync(Guid id, UpdateApplicationDto updateDto)
    {
        if (!HasWorkspace())
            return new(ApplicationOperationStatus.InvalidWorkspace);
        if (id == Guid.Empty)
            return new(ApplicationOperationStatus.InvalidRequest);

        var application = await _repository.GetByIdAsync(id);
        if (application is null)
            return new(ApplicationOperationStatus.NotFound);

        PortMapping? deployment = null;
        if (HasDeploymentChange(updateDto))
        {
            if (!updateDto.PortMappingId.HasValue || updateDto.PortMappingId == Guid.Empty ||
                !updateDto.TargetServerId.HasValue || updateDto.TargetServerId == Guid.Empty ||
                !updateDto.PortNumber.HasValue || updateDto.PortNumber is < 1 or > 65535)
                return new(ApplicationOperationStatus.InvalidRequest);

            deployment = await _repository.GetPortMappingAsync(updateDto.PortMappingId.Value);
            if (deployment is null || deployment.AppId != id)
                return new(ApplicationOperationStatus.DeploymentNotFound);

            var validation = await ValidateDeploymentAsync(
                updateDto.TargetServerId.Value,
                updateDto.PortNumber.Value,
                deployment.Id);
            if (validation != ApplicationOperationStatus.Success)
                return new(validation);

            deployment.ServerId = updateDto.TargetServerId.Value;
            deployment.PortNumber = updateDto.PortNumber.Value;
        }

        application.AppName = updateDto.AppName;
        application.OwnerTeam = updateDto.OwnerTeam;
        application.Risk = updateDto.Risk;
        application.Icon = updateDto.Icon;
        application.TechStack = updateDto.TechStack;

        try
        {
            await _repository.UpdateAsync(application, updateDto.Labels, deployment);
            var stored = await _repository.GetByIdAsync(id);
            return new(ApplicationOperationStatus.Success, Map(stored ?? application));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return new(ApplicationOperationStatus.PortCollision);
        }
    }

    private async Task<ApplicationOperationStatus> ValidateDeploymentAsync(
        Guid serverId,
        int portNumber,
        Guid? excludePortMappingId)
    {
        if (serverId == Guid.Empty || portNumber is < 1 or > 65535)
            return ApplicationOperationStatus.InvalidRequest;
        if (!await _repository.ServerExistsAsync(serverId))
            return ApplicationOperationStatus.ServerNotFound;
        if (await _repository.PortCollisionExistsAsync(serverId, portNumber, excludePortMappingId))
            return ApplicationOperationStatus.PortCollision;
        return ApplicationOperationStatus.Success;
    }

    private bool HasWorkspace() =>
        _tenantProvider.WorkspaceId.HasValue ;

    private static bool HasDeploymentChange(UpdateApplicationDto dto) =>
        dto.PortMappingId.HasValue || dto.TargetServerId.HasValue || dto.PortNumber.HasValue;

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static ApplicationResponseDto Map(AppEntity application) => new()
    {
        Id = application.Id,
        AppCode = application.AppCode,
        AppName = application.AppName,
        OwnerTeam = application.OwnerTeam,
        Risk = application.Risk,
        Icon = application.Icon,
        TechStack = application.TechStack,
        Servers = application.PortMappings.Select(mapping => new ServerOnApplicationDto
        {
            PortMappingId = mapping.Id,
            Id = mapping.ServerId,
            Hostname = mapping.Server?.Hostname ?? string.Empty,
            IpAddress = mapping.Server?.IpAddress ?? string.Empty,
            PortNumber = mapping.PortNumber,
            Protocol = mapping.Protocol
        }).ToList(),
        Labels = application.ApplicationLabels
            .Where(link => link.Label is not null)
            .Select(link => new LabelDto { Key = link.Label!.Key, Value = link.Label.Value })
            .ToList()
    };
}

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
    private readonly IScopedResourcePolicy _scopePolicy;
    private readonly ICurrentUserService _currentUser;

    public ApplicationService(IApplicationRepository repository, ITenantProvider tenantProvider, IScopedResourcePolicy scopePolicy, ICurrentUserService currentUser)
    {
        _repository = repository;
        _tenantProvider = tenantProvider;
        _scopePolicy = scopePolicy;
        _currentUser = currentUser;
    }

    public async Task<IEnumerable<ApplicationResponseDto>> GetAllAsync(string? labelKey = null, string? labelValue = null) =>
        HasWorkspace() ? await _repository.GetScopedAsync(await ReadableIdsAsync("application"), await ReadableIdsAsync("server"), labelKey: labelKey, labelValue: labelValue) : [];

    public async Task<IEnumerable<ApplicationResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids) =>
        HasWorkspace() ? await _repository.GetScopedAsync(await ReadableIdsAsync("application"), await ReadableIdsAsync("server"), ids) : [];

    public async Task<ApplicationResponseDto?> GetByIdAsync(Guid id)
    {
        if (!HasWorkspace() || id == Guid.Empty)
            return null;

        var application = await _repository.GetByIdAsync(id);
        if (application is not null && !await CanAsync(id, false)) return null;
        return application is null ? null : Map(application);
    }

    public async Task<ApplicationOperationResult> CreateAsync(CreateApplicationDto createDto)
    {
        if (!HasWorkspace())
            return new(ApplicationOperationStatus.InvalidWorkspace);
        if (!await CanCreateAsync(createDto.Labels)) return new(ApplicationOperationStatus.Forbidden);

        var appCode = createDto.AppCode.Trim().ToUpperInvariant();
        if (await _repository.AppCodeExistsAsync(appCode))
            return new(ApplicationOperationStatus.DuplicateAppCode);

        PortMapping? deployment = null;
        if (createDto.Deployment is not null)
        {
            if (!await CanResourceAsync("server", createDto.Deployment.ServerId, true))
                return new(ApplicationOperationStatus.Forbidden);
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
        if (!await CanAsync(id, true)) return new(ApplicationOperationStatus.Forbidden);
        if (updateDto.Labels is not null && !await CanCreateAsync(updateDto.Labels)) return new(ApplicationOperationStatus.Forbidden);

        PortMapping? deployment = null;
        if (HasDeploymentChange(updateDto))
        {
            if (!updateDto.PortMappingId.HasValue || updateDto.PortMappingId == Guid.Empty ||
                !updateDto.TargetServerId.HasValue || updateDto.TargetServerId == Guid.Empty ||
                !updateDto.PortNumber.HasValue || updateDto.PortNumber is < 1 or > 65535)
                return new(ApplicationOperationStatus.InvalidRequest);
            if (!await CanResourceAsync("server", updateDto.TargetServerId.Value, true))
                return new(ApplicationOperationStatus.Forbidden);

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

    private async Task<IEnumerable<ApplicationResponseDto>> FilterReadableAsync(IReadOnlyCollection<ApplicationResponseDto> applications)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId) || !_tenantProvider.WorkspaceId.HasValue) return [];
        var allowed = new List<ApplicationResponseDto>();
        foreach (var application in applications)
            if (await _scopePolicy.CanReadAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, "application", application.Id)) allowed.Add(application);
        return allowed;
    }

    private Task<IReadOnlySet<Guid>?> ReadableIdsAsync(string type) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId) || !_tenantProvider.WorkspaceId.HasValue
            ? Task.FromResult<IReadOnlySet<Guid>?>(new HashSet<Guid>())
            : _scopePolicy.GetReadableIdsAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, type);

    private Task<bool> CanAsync(Guid id, bool write) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId) || !_tenantProvider.WorkspaceId.HasValue
            ? Task.FromResult(false)
            : write
                ? _scopePolicy.CanWriteAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, "application", id)
                : _scopePolicy.CanReadAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, "application", id);

    private Task<bool> CanResourceAsync(string type, Guid id, bool write) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId) || !_tenantProvider.WorkspaceId.HasValue
            ? Task.FromResult(false)
            : write
                ? _scopePolicy.CanWriteAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, type, id)
                : _scopePolicy.CanReadAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, type, id);

    private Task<bool> CanCreateAsync(IReadOnlyCollection<LabelDto> labels) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId) || !_tenantProvider.WorkspaceId.HasValue
            ? Task.FromResult(false)
            : _scopePolicy.CanCreateAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, "application", labels);

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

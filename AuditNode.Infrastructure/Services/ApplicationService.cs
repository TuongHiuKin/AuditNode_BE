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
    private readonly ILabelAccessService _labelAccess;
    private readonly ILabelMutationCoordinator _mutationCoordinator;
    private readonly ICurrentUserService _currentUser;
    private readonly IGlobalCatalogRepository _catalog;
    private readonly IOwnerLabelService _ownerLabels;
    private readonly TimeProvider _timeProvider;

    public ApplicationService(IApplicationRepository repository, ILabelAccessService labelAccess, ILabelMutationCoordinator mutationCoordinator, ICurrentUserService currentUser, IGlobalCatalogRepository catalog, IOwnerLabelService ownerLabels, TimeProvider timeProvider)
    {
        _repository = repository;
        _labelAccess = labelAccess;
        _mutationCoordinator = mutationCoordinator;
        _currentUser = currentUser;
        _catalog = catalog;
        _ownerLabels = ownerLabels;
        _timeProvider = timeProvider;
    }

    public Task<CursorPageDto<ApplicationResponseDto>> GetCatalogPageAsync(CatalogPageQuery query, string? labelKey = null, string? labelValue = null, string? ownerUserId = null, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult(new CursorPageDto<ApplicationResponseDto>([], null, false))
            : _catalog.GetApplicationsAsync(_currentUser.UserId!, query, _timeProvider.GetUtcNow().UtcDateTime, labelKey, labelValue, ownerUserId, cancellationToken);

    public Task<ApplicationResponseDto?> GetCatalogDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult<ApplicationResponseDto?>(null)
            : _catalog.GetApplicationAsync(_currentUser.UserId!, id, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public Task<IReadOnlyList<ApplicationResponseDto>> ExportCatalogAsync(IReadOnlyCollection<Guid> ids, CatalogView view, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult<IReadOnlyList<ApplicationResponseDto>>([])
            : _catalog.ExportApplicationsAsync(_currentUser.UserId!, view, ids, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<IEnumerable<ApplicationResponseDto>> GetAllAsync(string? labelKey = null, string? labelValue = null) =>
        (await GetCatalogPageAsync(new CatalogPageQuery(CatalogView.Mine, 100), labelKey, labelValue)).Items;

    public async Task<IEnumerable<ApplicationResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids) =>
        await ExportCatalogAsync(ids.Where(id => id != Guid.Empty).Distinct().ToList(), CatalogView.Mine);

    public async Task<ApplicationResponseDto?> GetByIdAsync(Guid id)
    {
        return id == Guid.Empty ? null : await GetCatalogDetailAsync(id);
    }

    public async Task<ApplicationOperationResult> CreateAsync(CreateApplicationDto createDto)
    {
        var actor = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actor)) return new(ApplicationOperationStatus.Forbidden);

        var appCode = createDto.AppCode.Trim().ToUpperInvariant();
        if (await _repository.AppCodeExistsAsync(appCode, actor))
            return new(ApplicationOperationStatus.DuplicateAppCode);

        PortMapping? deployment = null;
        if (createDto.Deployment is not null)
        {
            var serverAccess = await _labelAccess.GetServerAccessAsync(createDto.Deployment.ServerId);
            if (serverAccess?.EffectivePermission != LabelEffectivePermission.Owner)
                return new(ApplicationOperationStatus.Forbidden);
            var validation = await ValidateDeploymentAsync(
                createDto.Deployment.ServerId,
                createDto.Deployment.PortNumber,
                actor,
                null);
            if (validation != ApplicationOperationStatus.Success)
                return new(validation);

            deployment = new PortMapping
            {
                Id = Guid.NewGuid(),
                OwnerUserId = actor,
                ServerId = createDto.Deployment.ServerId,
                PortNumber = createDto.Deployment.PortNumber,
                Protocol = createDto.Deployment.Protocol.Trim().ToUpperInvariant()
            };
        }

        await _ownerLabels.EnsureAsync(actor);

        var application = new AppEntity
        {
            Id = Guid.NewGuid(),
            OwnerUserId = actor,
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
        if (id == Guid.Empty)
            return new(ApplicationOperationStatus.InvalidRequest);

        var application = await _repository.GetByIdAsync(id);
        if (application is null)
            return new(ApplicationOperationStatus.NotFound);
        var access = await _labelAccess.GetApplicationAccessAsync(id);
        if (access is null) return new(ApplicationOperationStatus.NotFound);
        if (!access.Capabilities.CanEditProperties) return new(ApplicationOperationStatus.Forbidden);
        if (updateDto.Labels is not null && !access.Capabilities.CanChangeLabels) return new(ApplicationOperationStatus.Forbidden);

        PortMapping? deployment = null;
        Guid? originalServerId = null;
        if (HasDeploymentChange(updateDto))
        {
            if (!updateDto.PortMappingId.HasValue || updateDto.PortMappingId == Guid.Empty ||
                !updateDto.TargetServerId.HasValue || updateDto.TargetServerId == Guid.Empty ||
                !updateDto.PortNumber.HasValue || updateDto.PortNumber is < 1 or > 65535)
                return new(ApplicationOperationStatus.InvalidRequest);
            var serverAccess = await _labelAccess.GetServerAccessAsync(updateDto.TargetServerId.Value);
            if (serverAccess?.Capabilities.CanEditProperties != true || serverAccess.OwnerUserId != access.OwnerUserId)
                return new(ApplicationOperationStatus.Forbidden);

            deployment = await _repository.GetPortMappingAsync(updateDto.PortMappingId.Value);
            if (deployment is null || deployment.AppId != id)
                return new(ApplicationOperationStatus.DeploymentNotFound);
            originalServerId = deployment.ServerId;

            var validation = await ValidateDeploymentAsync(
                updateDto.TargetServerId.Value,
                updateDto.PortNumber.Value,
                access.OwnerUserId,
                deployment.Id);
            if (validation != ApplicationOperationStatus.Success)
                return new(validation);

        }

        try
        {
            var requiredServerIds = deployment is null
                ? Array.Empty<Guid>()
                : new[] { originalServerId!.Value, updateDto.TargetServerId!.Value }.Distinct().ToArray();
            var authorized = await _mutationCoordinator.ExecuteAsync(
                access.OwnerUserId,
                requiredServerIds,
                [id],
                async _ =>
                {
                    application.AppName = updateDto.AppName;
                    application.OwnerTeam = updateDto.OwnerTeam;
                    application.Risk = updateDto.Risk;
                    application.Icon = updateDto.Icon;
                    application.TechStack = updateDto.TechStack;
                    if (deployment is not null)
                    {
                        deployment.ServerId = updateDto.TargetServerId!.Value;
                        deployment.PortNumber = updateDto.PortNumber!.Value;
                    }
                    await _repository.UpdateAsync(application, updateDto.Labels, deployment);
                });
            if (!authorized) return new(ApplicationOperationStatus.Forbidden);
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
        string ownerUserId,
        Guid? excludePortMappingId)
    {
        if (serverId == Guid.Empty || portNumber is < 1 or > 65535)
            return ApplicationOperationStatus.InvalidRequest;
        if (!await _repository.ServerExistsAsync(serverId, ownerUserId))
            return ApplicationOperationStatus.ServerNotFound;
        if (await _repository.PortCollisionExistsAsync(serverId, portNumber, ownerUserId, excludePortMappingId))
            return ApplicationOperationStatus.PortCollision;
        return ApplicationOperationStatus.Success;
    }

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
            .ToList(),
        OwnerUserId = application.OwnerUserId ?? string.Empty,
        EffectivePermission = LabelEffectivePermission.Owner,
        Capabilities = CatalogCapabilities.Owner
    };
}

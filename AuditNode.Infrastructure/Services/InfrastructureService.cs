using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AuditNode.Infrastructure.Services;

public class InfrastructureService : IInfrastructureService
{
    private readonly AuditDbContext _context;
    private readonly ILogger<InfrastructureService> _logger;
    private readonly ILabelAccessService _labelAccess;
    private readonly ILabelMutationCoordinator _mutationCoordinator;
    private readonly ICurrentUserService _currentUser;
    private readonly IGlobalCatalogRepository _catalog;
    private readonly TimeProvider _timeProvider;

    public InfrastructureService(AuditDbContext context, ILogger<InfrastructureService> logger, ILabelAccessService labelAccess, ILabelMutationCoordinator mutationCoordinator, ICurrentUserService currentUser, IGlobalCatalogRepository catalog, TimeProvider timeProvider)
    {
        _context = context;
        _logger = logger;
        _labelAccess = labelAccess;
        _mutationCoordinator = mutationCoordinator;
        _currentUser = currentUser;
        _catalog = catalog;
        _timeProvider = timeProvider;
    }

    public Task<int?> GetDependenciesCountCatalogAsync(Guid appId, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult<int?>(null)
            : _catalog.GetDependencyCountAsync(_currentUser.UserId!, appId, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public Task<IReadOnlyList<DeployedAppDto>?> GetDeployedAppsByServerCatalogAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult<IReadOnlyList<DeployedAppDto>?>(null)
            : _catalog.GetDeployedApplicationsAsync(_currentUser.UserId!, serverId, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<int> GetDependenciesCountAsync(Guid appId)
    {
        if (!await CanReadAsync("application", appId)) return 0;
        _logger.LogInformation("Checking dependency count for application {AppId}", appId);

        // 1. Get all port mapping IDs for this specific app
        var portMappingIds = await _context.PortMappings
            .Where(pm => pm.AppId == appId)
            .Select(pm => pm.Id)
            .ToListAsync();

        // 2. Count connections where this app is either the caller (Source) OR the receiver (Inbound)
        var count = await _context.AppDependencies
            .CountAsync(ad => ad.SourceAppId == appId || 
                             ad.DestAppId == appId || 
                             portMappingIds.Contains(ad.DestPortId));

        return count;
    }

    public async Task<DeploymentOperationStatus> MigrateAppAsync(MigrateAppDto migrateDto)
    {
        _logger.LogInformation("Starting migration for PortMapping {PortMappingId}...", migrateDto.PortMappingId);

        if (migrateDto.PortMappingId == Guid.Empty ||
            migrateDto.TargetServerId == Guid.Empty ||
            migrateDto.NewPortNumber is < 1 or > 65535)
            return DeploymentOperationStatus.InvalidRequest;

        var portMapping = await _context.PortMappings
            .FirstOrDefaultAsync(pm => pm.Id == migrateDto.PortMappingId);
        if (portMapping is null)
            return DeploymentOperationStatus.NotFound;

        var applicationAccess = await _labelAccess.GetApplicationAccessAsync(portMapping.AppId);
        var sourceServerAccess = await _labelAccess.GetServerAccessAsync(portMapping.ServerId);
        var targetServerAccess = await _labelAccess.GetServerAccessAsync(migrateDto.TargetServerId);
        if (applicationAccess is null || sourceServerAccess is null)
            return DeploymentOperationStatus.NotFound;
        if (targetServerAccess is null)
            return DeploymentOperationStatus.ServerNotFound;
        if (!applicationAccess.Capabilities.CanEditProperties ||
            !sourceServerAccess.Capabilities.CanEditProperties ||
            !targetServerAccess.Capabilities.CanEditProperties ||
            applicationAccess.OwnerUserId != sourceServerAccess.OwnerUserId ||
            applicationAccess.OwnerUserId != targetServerAccess.OwnerUserId)
            return DeploymentOperationStatus.Forbidden;

        if (!await _context.Servers.AnyAsync(server => server.Id == migrateDto.TargetServerId))
            return DeploymentOperationStatus.ServerNotFound;

        if (await _context.PortMappings.AnyAsync(mapping =>
                mapping.ServerId == migrateDto.TargetServerId &&
                mapping.PortNumber == migrateDto.NewPortNumber &&
                mapping.Id != migrateDto.PortMappingId))
            return DeploymentOperationStatus.PortCollision;

        try
        {
            var sourceServerId = portMapping.ServerId;
            var authorized = await _mutationCoordinator.ExecuteAsync(
                applicationAccess.OwnerUserId,
                new[] { sourceServerId, migrateDto.TargetServerId }.Distinct().ToArray(),
                [portMapping.AppId],
                async _ =>
                {
                    var isNetworkModified = false;
                    if (portMapping.ServerId != migrateDto.TargetServerId)
                    {
                        portMapping.ServerId = migrateDto.TargetServerId;
                        isNetworkModified = true;
                        _logger.LogInformation("Server ID changed to {ServerId}", migrateDto.TargetServerId);
                    }
                    if (portMapping.PortNumber != migrateDto.NewPortNumber)
                    {
                        portMapping.PortNumber = migrateDto.NewPortNumber;
                        isNetworkModified = true;
                        _logger.LogInformation("Port Number changed to {Port}", migrateDto.NewPortNumber);
                    }
                    if (!isNetworkModified) return;
                    _context.PortMappings.Update(portMapping);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully updated port mapping {PortMappingId}", migrateDto.PortMappingId);
                });
            if (!authorized) return DeploymentOperationStatus.Forbidden;
            return DeploymentOperationStatus.Success;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return DeploymentOperationStatus.PortCollision;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during migration for PortMapping {PortMappingId}. Rolling back.", migrateDto.PortMappingId);
            throw;
        }
    }

    public async Task<bool> PurgeAppAsync(Guid appId)
    {
        _logger.LogInformation("Starting cascading purge for application {AppId}", appId);

        var access = await _labelAccess.GetApplicationAccessAsync(appId);
        if (access?.Capabilities.CanDelete != true) return false;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 0. Pre-fetch port mapping IDs for the app
            var portMappingIds = await _context.PortMappings
                .Where(pm => pm.AppId == appId)
                .Select(pm => pm.Id)
                .ToListAsync();

            // 1. Delete all records from app_dependencies where app is SOURCE or DESTINATION
            var dependenciesToDelete = await _context.AppDependencies
                .Where(ad => ad.SourceAppId == appId || 
                             ad.DestAppId == appId || 
                             portMappingIds.Contains(ad.DestPortId))
                .ToListAsync();
            
            if (dependenciesToDelete.Any())
            {
                _context.AppDependencies.RemoveRange(dependenciesToDelete);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Deleted {Count} dependencies for application {AppId}", dependenciesToDelete.Count, appId);
            }

            // 2. Delete all rows from port_mappings where app_id == id
            var portMappingsToDelete = await _context.PortMappings
                .Where(pm => pm.AppId == appId)
                .ToListAsync();

            if (portMappingsToDelete.Any())
            {
                _context.PortMappings.RemoveRange(portMappingsToDelete);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Deleted {Count} port mappings for application {AppId}", portMappingsToDelete.Count, appId);
            }

            // 3. Delete the root record from applications where id == id
            var appToDelete = await _context.Applications.FindAsync(appId);
            if (appToDelete != null)
            {
                _context.Applications.Remove(appToDelete);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Deleted root application {AppId}", appId);
            }
            else
            {
                _logger.LogWarning("Application {AppId} not found during purge", appId);
                await transaction.RollbackAsync();
                return false;
            }

            // 4. Commit transaction safely
            await transaction.CommitAsync();
            _logger.LogInformation("Successfully purged application {AppId} and all its connections", appId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during cascading purge of application {AppId}. Rolling back.", appId);
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IEnumerable<DeployedAppDto>> GetDeployedAppsByServerAsync(Guid serverId)
    {
        _logger.LogInformation("Fetching deployed applications for server {ServerId}", serverId);

        return await GetDeployedAppsByServerCatalogAsync(serverId) ?? [];
    }

    private async Task<bool> CanReadAsync(string type, Guid id) => type == "server"
        ? (await _labelAccess.GetServerAccessAsync(id))?.Capabilities.CanRead == true
        : (await _labelAccess.GetApplicationAccessAsync(id))?.Capabilities.CanRead == true;
}

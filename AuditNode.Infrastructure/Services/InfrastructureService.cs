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
    private readonly IScopedResourcePolicy _policy;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantProvider _tenant;

    public InfrastructureService(AuditDbContext context, ILogger<InfrastructureService> logger, IScopedResourcePolicy policy, ICurrentUserService currentUser, ITenantProvider tenant)
    {
        _context = context;
        _logger = logger;
        _policy = policy;
        _currentUser = currentUser;
        _tenant = tenant;
    }

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

        if (!await _context.Servers.AnyAsync(server => server.Id == migrateDto.TargetServerId))
            return DeploymentOperationStatus.ServerNotFound;

        if (await _context.PortMappings.AnyAsync(mapping =>
                mapping.ServerId == migrateDto.TargetServerId &&
                mapping.PortNumber == migrateDto.NewPortNumber &&
                mapping.Id != migrateDto.PortMappingId))
            return DeploymentOperationStatus.PortCollision;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            bool isNetworkModified = false;

            // Independent assignment: Target Server
            if (migrateDto.TargetServerId != Guid.Empty && portMapping.ServerId != migrateDto.TargetServerId)
            {
                portMapping.ServerId = migrateDto.TargetServerId;
                isNetworkModified = true;
                _logger.LogInformation("Server ID changed to {ServerId}", migrateDto.TargetServerId);
            }

            // Independent assignment: Port Number
            if (portMapping.PortNumber != migrateDto.NewPortNumber)
            {
                portMapping.PortNumber = migrateDto.NewPortNumber;
                isNetworkModified = true;
                _logger.LogInformation("Port Number changed to {Port}", migrateDto.NewPortNumber);
            }

            if (isNetworkModified)
            {
                // Explicitly notify the change tracker
                _context.PortMappings.Update(portMapping);
                
                // CRITICAL: Ensure SaveChanges is called within the transaction
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Successfully updated port mapping {PortMappingId}", migrateDto.PortMappingId);
            }
            else
            {
                _logger.LogInformation("No network residency changes detected for port mapping {PortMappingId}", migrateDto.PortMappingId);
                await transaction.RollbackAsync(); // Nothing to do
            }

            return DeploymentOperationStatus.Success;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync();
            return DeploymentOperationStatus.PortCollision;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during migration for PortMapping {PortMappingId}. Rolling back.", migrateDto.PortMappingId);
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> PurgeAppAsync(Guid appId)
    {
        _logger.LogInformation("Starting cascading purge for application {AppId}", appId);

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

        if (!await CanReadAsync("server", serverId)) return [];
        var allowedApps = await ReadableIdsAsync("application");
        return await _context.PortMappings
            .Where(pm => pm.ServerId == serverId && (allowedApps == null || allowedApps.Contains(pm.AppId)))
            .Include(pm => pm.Application)
            .Select(pm => new DeployedAppDto
            {
                PortMappingId = pm.Id,
                AppId = pm.AppId,
                AppCode = pm.Application!.AppCode,
                AppName = pm.Application!.AppName,
                PortNumber = pm.PortNumber
            })
            .ToListAsync();
    }

    private Task<bool> CanReadAsync(string type, Guid id) =>
        !_tenant.WorkspaceId.HasValue || string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult(false)
            : _policy.CanReadAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, type, id);

    private Task<IReadOnlySet<Guid>?> ReadableIdsAsync(string type) =>
        !_tenant.WorkspaceId.HasValue || string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult<IReadOnlySet<Guid>?>(new HashSet<Guid>())
            : _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, type);
}

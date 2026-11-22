using System.Data;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AuditNode.Infrastructure.Services;

public class DependencyService : IDependencyService
{
    private readonly AuditDbContext _dbContext;
    private readonly ILogger<DependencyService> _logger;
    private readonly ICurrentUserService _currentUser;

    public DependencyService(
        AuditDbContext dbContext,
        ILogger<DependencyService> logger,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<DependencySyncStatus> SyncDependenciesAsync(SyncDependenciesDto dto)
    {
        if (dto is null) return DependencySyncStatus.InvalidRequest;
        var payload = dto.Dependencies;
        if (payload is null || dto.Version is null || dto.Version < 0 ||
            string.IsNullOrWhiteSpace(_currentUser.UserId))
            return DependencySyncStatus.InvalidRequest;
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : null;
        if (payload.Any(item =>
                item.SourceAppId == Guid.Empty || item.DestAppId == Guid.Empty || item.DestinationPortMappingId == Guid.Empty))
            return DependencySyncStatus.InvalidRequest;
        if (payload.Any(item => item.SourceAppId == item.DestAppId))
            return DependencySyncStatus.SelfLoop;

        var keys = payload.Select(Key).ToArray();
        if (keys.Distinct().Count() != keys.Length)
            return DependencySyncStatus.Duplicate;

        var appIds = payload.SelectMany(item => new[] { item.SourceAppId, item.DestAppId }).Distinct().ToArray();
        string ownerUserId;
        if (appIds.Length == 0)
        {
            ownerUserId = _currentUser.UserId!;
        }
        else
        {
            var applications = await _dbContext.Applications.IgnoreQueryFilters().AsNoTracking()
                .Where(application => appIds.Contains(application.Id))
                .Select(application => new { application.Id, application.OwnerUserId })
                .ToListAsync();
            if (applications.Count != appIds.Length)
                return DependencySyncStatus.NotFound;
            var ownerIds = applications.Select(item => item.OwnerUserId).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToList();
            if (ownerIds.Count != 1 || applications.Any(item => item.OwnerUserId is null))
                return DependencySyncStatus.Forbidden;
            ownerUserId = ownerIds[0]!;
        }

        // This endpoint replaces the owner's complete dependency set. Scoped editors must use
        // topology delta commands so they cannot delete dependencies outside their grants.
        if (!string.Equals(ownerUserId, _currentUser.UserId, StringComparison.Ordinal))
            return DependencySyncStatus.Forbidden;

        var ownerState = await LockOwnerStateAsync(ownerUserId);
        if (ownerState.TopologyVersion != dto.Version.Value) return DependencySyncStatus.Conflict;

        var destinationPortIds = payload.Select(item => item.DestinationPortMappingId).Distinct().ToArray();
        var destinationPorts = await _dbContext.PortMappings.IgnoreQueryFilters()
            .Where(mapping => mapping.OwnerUserId == ownerUserId && destinationPortIds.Contains(mapping.Id))
            .Select(mapping => new { mapping.Id, mapping.AppId })
            .ToDictionaryAsync(mapping => mapping.Id);
        if (destinationPorts.Count != destinationPortIds.Length)
            return DependencySyncStatus.NotFound;
        if (payload.Any(item => destinationPorts[item.DestinationPortMappingId].AppId != item.DestAppId))
            return DependencySyncStatus.DestinationMismatch;

        var existing = await _dbContext.AppDependencies.IgnoreQueryFilters()
            .Where(item => item.OwnerUserId == ownerUserId).ToListAsync();
        var desiredKeys = keys.ToHashSet();
        var canonicalExisting = existing.GroupBy(Key).ToDictionary(group => group.Key, group => group.First());
        var toDelete = existing
            .Where(dependency => !desiredKeys.Contains(Key(dependency)) ||
                                 canonicalExisting[Key(dependency)].Id != dependency.Id)
            .ToList();
        var toInsert = payload
            .Where(item => !canonicalExisting.ContainsKey(Key(item)))
            .Select(item => new AppDependency
            {
                Id = Guid.NewGuid(),
                OwnerUserId = ownerUserId,
                SourceAppId = item.SourceAppId,
                DestAppId = item.DestAppId,
                DestPortId = item.DestinationPortMappingId,
                ConnectionType = "Automatic",
                CreatedAt = DateTime.UtcNow
            }).ToList();
        var referencedIds = await _dbContext.TopologyEdges.IgnoreQueryFilters()
            .Where(edge => edge.OwnerUserId == ownerUserId && edge.ReferenceId.HasValue)
            .Select(edge => edge.ReferenceId!.Value).ToListAsync();
        if (toDelete.Any(item => referencedIds.Contains(item.Id)))
            return DependencySyncStatus.InvalidRequest;
        try
        {
            _dbContext.AppDependencies.RemoveRange(toDelete);
            await _dbContext.AppDependencies.AddRangeAsync(toInsert);
            ownerState.TopologyVersion++;
            ownerState.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            if (transaction is not null) await transaction.CommitAsync();
            return DependencySyncStatus.Success;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Dependency synchronization failed.");
            if (transaction is not null) await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<OwnerCatalogState> LockOwnerStateAsync(string ownerUserId)
    {
        if (_dbContext.Database.IsRelational())
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO owner_catalog_states (owner_user_id, topology_version, updated_at) VALUES ({ownerUserId}, 0, CURRENT_TIMESTAMP) ON CONFLICT (owner_user_id) DO NOTHING");
            return await _dbContext.OwnerCatalogStates.FromSqlInterpolated(
                    $"SELECT * FROM owner_catalog_states WHERE owner_user_id = {ownerUserId} FOR UPDATE")
                .SingleAsync();
        }

        var state = await _dbContext.OwnerCatalogStates.SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId);
        if (state is not null) return state;
        state = new OwnerCatalogState { OwnerUserId = ownerUserId };
        _dbContext.OwnerCatalogStates.Add(state);
        return state;
    }

    private static string Key(DependencyItemDto item) =>
        $"{item.SourceAppId:N}|{item.DestAppId:N}|{item.DestinationPortMappingId:N}";

    private static string Key(AppDependency item) =>
        $"{item.SourceAppId:N}|{item.DestAppId:N}|{item.DestPortId:N}";
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AuditNode.Infrastructure.Services;

public class DependencyService : IDependencyService
{
    private readonly AuditDbContext _dbContext;
    private readonly ILogger<DependencyService> _logger;

    public DependencyService(AuditDbContext dbContext, ILogger<DependencyService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<DependencySyncStatus> SyncDependenciesAsync(SyncDependenciesDto dto)
    {
        var payload = dto?.Dependencies;
        if (payload is null || !_dbContext.CurrentWorkspaceId.HasValue || _dbContext.CurrentWorkspaceId == Guid.Empty)
            return DependencySyncStatus.InvalidRequest;
        if (payload.Any(item =>
                item.SourceAppId == Guid.Empty || item.DestAppId == Guid.Empty || item.DestinationPortMappingId == Guid.Empty))
            return DependencySyncStatus.InvalidRequest;
        if (payload.Any(item => item.SourceAppId == item.DestAppId))
            return DependencySyncStatus.SelfLoop;

        var keys = payload.Select(Key).ToArray();
        if (keys.Distinct().Count() != keys.Length)
            return DependencySyncStatus.Duplicate;

        var appIds = payload.SelectMany(item => new[] { item.SourceAppId, item.DestAppId }).Distinct().ToArray();
        var visibleAppIds = await _dbContext.Applications
            .Where(application => appIds.Contains(application.Id))
            .Select(application => application.Id)
            .ToListAsync();
        if (visibleAppIds.Count != appIds.Length)
            return DependencySyncStatus.NotFound;

        var destinationPortIds = payload.Select(item => item.DestinationPortMappingId).Distinct().ToArray();
        var destinationPorts = await _dbContext.PortMappings
            .Where(mapping => destinationPortIds.Contains(mapping.Id))
            .Select(mapping => new { mapping.Id, mapping.AppId })
            .ToDictionaryAsync(mapping => mapping.Id, mapping => mapping.AppId);
        if (destinationPorts.Count != destinationPortIds.Length)
            return DependencySyncStatus.NotFound;
        if (payload.Any(item => destinationPorts[item.DestinationPortMappingId] != item.DestAppId))
            return DependencySyncStatus.DestinationMismatch;

        var existing = await _dbContext.AppDependencies.ToListAsync();
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
                SourceAppId = item.SourceAppId,
                DestAppId = item.DestAppId,
                DestPortId = item.DestinationPortMappingId,
                ConnectionType = "Automatic",
                CreatedAt = DateTime.UtcNow
            }).ToList();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            _dbContext.AppDependencies.RemoveRange(toDelete);
            await _dbContext.AppDependencies.AddRangeAsync(toInsert);
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return DependencySyncStatus.Success;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Dependency synchronization failed.");
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static string Key(DependencyItemDto item) =>
        $"{item.SourceAppId:N}|{item.DestAppId:N}|{item.DestinationPortMappingId:N}";

    private static string Key(AppDependency item) =>
        $"{item.SourceAppId:N}|{item.DestAppId:N}|{item.DestPortId:N}";
}

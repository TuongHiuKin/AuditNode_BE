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

    public async Task SyncDependenciesAsync(SyncDependenciesDto dto)
    {
        var payload = dto?.Dependencies ?? new List<DependencyItemDto>();
        _logger.LogInformation("Received {Count} dependencies for sync.", payload.Count);

        // 1. Fetch all existing records from app_dependencies
        var existingDb = await _dbContext.AppDependencies.ToListAsync();

        // 2. Calculate connectionsToDelete: DB items not in Payload (Match by SourceAppId, DestAppId & DestPortId)
        var toDelete = existingDb
            .Where(db => !payload.Any(p => 
                p.SourceAppId == db.SourceAppId && 
                p.DestAppId == db.DestAppId && 
                p.DestPortId == db.DestPortId))
            .ToList();

        // 3. Calculate connectionsToInsert: Payload items not in DB
        var toInsertDto = payload
            .Where(p => !existingDb.Any(db => 
                db.SourceAppId == p.SourceAppId && 
                db.DestAppId == p.DestAppId && 
                db.DestPortId == p.DestPortId))
            .ToList();

        var toInsertEntities = toInsertDto.Select(incoming => new AppDependency
        {
            Id = Guid.NewGuid(),
            SourceAppId = incoming.SourceAppId,
            DestAppId = incoming.DestAppId,
            DestPortId = incoming.DestPortId,
            ConnectionType = "Automatic",
            CreatedAt = DateTime.UtcNow
        }).ToList();

        _logger.LogInformation("Delta calculated: {InsertCount} to insert, {DeleteCount} to delete.", 
            toInsertEntities.Count, toDelete.Count);

        // 4. Wrap the operations in a Database Transaction
        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            if (toDelete.Any())
            {
                _dbContext.AppDependencies.RemoveRange(toDelete);
            }

            if (toInsertEntities.Any())
            {
                await _dbContext.AppDependencies.AddRangeAsync(toInsertEntities);
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            
            _logger.LogInformation("Sync transaction committed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during dependency sync transaction.");
            await transaction.RollbackAsync();
            throw;
        }
    }
}

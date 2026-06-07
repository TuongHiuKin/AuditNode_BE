using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public class DependencyService : IDependencyService
{
    private readonly AuditDbContext _dbContext;

    public DependencyService(AuditDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SyncDependenciesAsync(SyncDependenciesDto dto)
    {
        // 1. Fetch all existing records from app_dependencies
        var existingDependencies = await _dbContext.AppDependencies.ToListAsync();

        // 2. Calculate connectionsToDelete: Existing records that do NOT exist in the incoming DTO payload
        // Match by SourceAppId & DestAppId
        var connectionsToDelete = existingDependencies
            .Where(existing => !dto.Dependencies.Any(incoming => 
                incoming.SourceAppId == existing.SourceAppId && 
                incoming.DestAppId == existing.DestAppId))
            .ToList();

        // 3. Calculate connectionsToInsert: Incoming payload items that do NOT exist in the database
        var connectionsToInsert = dto.Dependencies
            .Where(incoming => !existingDependencies.Any(existing => 
                existing.SourceAppId == incoming.SourceAppId && 
                existing.DestAppId == incoming.DestAppId))
            .Select(incoming => new AppDependency
            {
                Id = Guid.NewGuid(),
                SourceAppId = incoming.SourceAppId,
                DestAppId = incoming.DestAppId,
                ConnectionType = "Automatic", // Default connection type
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        // 4. Wrap the operations in a Database Transaction
        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            if (connectionsToDelete.Any())
            {
                _dbContext.AppDependencies.RemoveRange(connectionsToDelete);
            }

            if (connectionsToInsert.Any())
            {
                await _dbContext.AppDependencies.AddRangeAsync(connectionsToInsert);
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

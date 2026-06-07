using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Repositories;

public class DependencyRepository : IDependencyRepository
{
    private readonly AuditDbContext _context;

    public DependencyRepository(AuditDbContext context)
    {
        _context = context;
    }

    public async Task SyncDependenciesAsync(SyncDependenciesDto syncDto)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Fetch existing dependencies
            var existingDependencies = await _context.AppDependencies.ToListAsync();

            // 2. Identify deletions
            var toDelete = existingDependencies
                .Where(existing => !syncDto.Dependencies.Any(incoming => 
                    incoming.SourceAppId == existing.SourceAppId && 
                    incoming.DestAppId == existing.DestAppId))
                .ToList();

            // 3. Identify insertions
            var toInsert = syncDto.Dependencies
                .Where(incoming => !existingDependencies.Any(existing => 
                    existing.SourceAppId == incoming.SourceAppId && 
                    existing.DestAppId == incoming.DestAppId))
                .Select(incoming => new AppDependency
                {
                    Id = Guid.NewGuid(),
                    SourceAppId = incoming.SourceAppId,
                    DestAppId = incoming.DestAppId,
                    ConnectionType = incoming.ConnectionType,
                    DestPortId = Guid.Empty // Default for schema integrity
                })
                .ToList();

            // 4. Execute Delta
            if (toDelete.Any())
            {
                _context.AppDependencies.RemoveRange(toDelete);
            }

            if (toInsert.Any())
            {
                await _context.AppDependencies.AddRangeAsync(toInsert);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

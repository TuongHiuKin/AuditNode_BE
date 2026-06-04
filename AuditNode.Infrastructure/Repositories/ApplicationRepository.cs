using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Infrastructure.Repositories;

public class ApplicationRepository : IApplicationRepository
{
    private readonly AuditDbContext _dbContext;

    public ApplicationRepository(AuditDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<ApplicationResponseDto>> GetApplicationsAsync()
    {
        return await _dbContext.Applications
            .Include(a => a.PortMappings)
                .ThenInclude(pm => pm.Server)
            .Select(a => new ApplicationResponseDto
            {
                Id = a.Id,
                AppCode = a.AppCode,
                AppName = a.AppName,
                OwnerTeam = a.OwnerTeam,
                Risk = a.Risk,
                Icon = a.Icon,
                TechStack = a.TechStack,
                Servers = a.PortMappings.Select(pm => new ServerOnApplicationDto
                {
                    Id = pm.Server!.Id,
                    Hostname = pm.Server!.Hostname,
                    IpAddress = pm.Server!.IpAddress,
                    PortNumber = pm.PortNumber,
                    Protocol = pm.Protocol
                }).ToList()
            })
            .ToListAsync();
    }

    public async Task<AppEntity?> GetByIdAsync(Guid id)
    {
        return await _dbContext.Applications
            .Include(a => a.PortMappings)
                .ThenInclude(pm => pm.Server)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task UpdateAsync(AppEntity application)
    {
        _dbContext.Entry(application).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<AppEntity> RegisterApplicationAsync(AppEntity application)
    {
        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var existingApp = await _dbContext.Applications
                .FirstOrDefaultAsync(a => a.AppCode == application.AppCode);

            if (existingApp == null)
            {
                _dbContext.Applications.Add(application);
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                return application;
            }
            else
            {
                // Update non-key fields if business rules dictate
                existingApp.AppName = application.AppName;
                existingApp.OwnerTeam = application.OwnerTeam;
                existingApp.Risk = application.Risk;
                existingApp.Icon = application.Icon;
                existingApp.TechStack = application.TechStack;

                // Transfer port mappings to existing app
                foreach (var pm in application.PortMappings)
                {
                    pm.AppId = existingApp.Id;
                    _dbContext.PortMappings.Add(pm);
                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                return existingApp;
            }
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

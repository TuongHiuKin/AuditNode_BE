using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
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
        var query = _dbContext.Applications
            .Include(a => a.PortMappings)
                .ThenInclude(pm => pm.Server);
        
        return await MapToResponseDto(query).ToListAsync();
    }

    public async Task<IEnumerable<ApplicationResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        var query = _dbContext.Applications
            .Include(a => a.PortMappings)
                .ThenInclude(pm => pm.Server)
            .Where(a => ids.Contains(a.Id));

        return await MapToResponseDto(query).ToListAsync();
    }

    private IQueryable<ApplicationResponseDto> MapToResponseDto(IQueryable<AppEntity> query)
    {
        return query.Select(a => new ApplicationResponseDto
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
        });
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

    public async Task<bool> UpdateApplicationWithNetworkAsync(Guid id, UpdateApplicationDto updateDto)
    {
        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            // Step 1: Fetch and update Application metadata
            var app = await _dbContext.Applications.FirstOrDefaultAsync(a => a.Id == id);
            if (app == null) return false;

            app.AppName = updateDto.AppName;
            app.OwnerTeam = updateDto.OwnerTeam;
            app.Risk = updateDto.Risk;
            app.Icon = updateDto.Icon;
            app.TechStack = updateDto.TechStack;

            // Step 2: Flexible PortMapping Update Logic
            var portMapping = await _dbContext.PortMappings.FirstOrDefaultAsync(p => p.AppId == id);
            
            if (updateDto.TargetServerId.HasValue || updateDto.PortNumber.HasValue)
            {
                if (portMapping == null)
                {
                    // Create new mapping if none exists
                    portMapping = new PortMapping
                    {
                        Id = Guid.NewGuid(),
                        AppId = id,
                        ServerId = updateDto.TargetServerId ?? Guid.Empty,
                        PortNumber = updateDto.PortNumber ?? 0,
                        Protocol = "TCP"
                    };
                    _dbContext.PortMappings.Add(portMapping);
                }
                else
                {
                    bool isNetworkModified = false;

                    // Independent Update: Server ID
                    if (updateDto.TargetServerId.HasValue && updateDto.TargetServerId.Value != Guid.Empty && portMapping.ServerId != updateDto.TargetServerId.Value)
                    {
                        portMapping.ServerId = updateDto.TargetServerId.Value;
                        isNetworkModified = true;
                    }

                    // Independent Update: Port Number
                    if (updateDto.PortNumber.HasValue && portMapping.PortNumber != updateDto.PortNumber.Value)
                    {
                        portMapping.PortNumber = updateDto.PortNumber.Value;
                        isNetworkModified = true;
                    }

                    if (isNetworkModified)
                    {
                        _dbContext.PortMappings.Update(portMapping);
                    }
                }
            }

            // Step 3: Guaranteed Save and Commit
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return true;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
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

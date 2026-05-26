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
                OwnerId = a.OwnerId,
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

    public async Task<AppEntity> CreateApplicationAsync(AppEntity application)
    {
        _dbContext.Applications.Add(application);
        await _dbContext.SaveChangesAsync();
        return application;
    }
}

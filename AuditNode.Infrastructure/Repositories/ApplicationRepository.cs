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
            .Select(a => new ApplicationResponseDto
            {
                Id = a.Id,
                AppCode = a.AppCode,
                AppName = a.AppName,
                OwnerId = a.OwnerId
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

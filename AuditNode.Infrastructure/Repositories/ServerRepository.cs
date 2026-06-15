using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace AuditNode.Infrastructure.Repositories;

public class ServerRepository : IServerRepository
{
    private readonly AuditDbContext _dbContext;

    public ServerRepository(AuditDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<ServerResponseDto>> GetAllWithAppsAsync(string? environment = null, Guid? datacenterId = null)
    {
        var query = _dbContext.Servers
            .Include(s => s.Datacenter)
            .Include(s => s.PortMappings)
            .ThenInclude(pm => pm.Application)
            .AsQueryable();

        if (!string.IsNullOrEmpty(environment))
        {
            query = query.Where(s => s.Environment == environment);
        }

        if (datacenterId.HasValue && datacenterId != Guid.Empty)
        {
            query = query.Where(s => s.DatacenterId == datacenterId.Value);
        }

        return await MapToResponseDto(query).ToListAsync();
    }

    public async Task<IEnumerable<ServerResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        var query = _dbContext.Servers
            .Include(s => s.Datacenter)
            .Include(s => s.PortMappings)
            .ThenInclude(pm => pm.Application)
            .Where(s => ids.Contains(s.Id));

        return await MapToResponseDto(query).ToListAsync();
    }

    private IQueryable<ServerResponseDto> MapToResponseDto(IQueryable<Server> query)
    {
        return query.Select(s => new ServerResponseDto
        {
            Id = s.Id,
            DatacenterId = s.DatacenterId,
            IpAddress = s.IpAddress,
            Hostname = s.Hostname,
            OsType = s.OsType,
            Environment = s.Environment,
            Datacenter = s.Datacenter != null ? s.Datacenter.Name : string.Empty,
            Status = s.Status,
            Applications = s.PortMappings.Select(pm => new ApplicationOnServerDto
            {
                Id = pm.Application!.Id,
                AppCode = pm.Application.AppCode,
                AppName = pm.Application.AppName,
                OwnerTeam = pm.Application.OwnerTeam,
                PortNumber = pm.PortNumber,
                Protocol = pm.Protocol
            }).ToList()
        });
    }

    public async Task<Server?> GetByIdAsync(Guid id)
    {
        return await _dbContext.Servers
            .Include(s => s.Datacenter)
            .Include(s => s.PortMappings)
            .ThenInclude(pm => pm.Application)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task UpdateAsync(Server server)
    {
        _dbContext.Entry(server).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<Server> CreateServerAsync(Server server)
    {
        _dbContext.Servers.Add(server);
        await _dbContext.SaveChangesAsync();
        return server;
    }
}

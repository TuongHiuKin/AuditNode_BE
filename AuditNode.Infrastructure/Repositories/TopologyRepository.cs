using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Repositories;

public class TopologyRepository : ITopologyRepository
{
    private readonly AuditDbContext _context;

    public TopologyRepository(AuditDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<TopologyTreeDto>> GetTopologyTreeAsync(Guid? datacenterId, int skip, int take)
    {
        var query = _context.Datacenters
            .Include(d => d.Servers)
                .ThenInclude(s => s.PortMappings)
                    .ThenInclude(pm => pm.Application)
            .AsNoTracking();

        if (datacenterId.HasValue)
        {
            query = query.Where(d => d.Id == datacenterId.Value);
        }

        var datacenters = await query
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return datacenters.Select(d => new TopologyTreeDto
        {
            Id = d.Id,
            Name = d.Name,
            Location = d.Location,
            Servers = d.Servers.Select(s => new ServerNodeDto
            {
                Id = s.Id,
                Hostname = s.Hostname,
                IpAddress = s.IpAddress,
                Applications = s.PortMappings.Select(pm => new ApplicationNodeDto
                {
                    Id = pm.Application!.Id,
                    Name = pm.Application.AppName,
                    Port = pm.PortNumber,
                    Protocol = pm.Protocol
                }).ToList()
            }).ToList()
        });
    }

    public async Task<DependencyMapDto> GetDependencyMapAsync(string? environment = null, Guid? datacenterId = null)
    {
        var serverQuery = _context.Servers
            .Include(s => s.PortMappings)
                .ThenInclude(pm => pm.Application)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(environment))
        {
            serverQuery = serverQuery.Where(s => s.Environment == environment);
        }

        if (datacenterId.HasValue && datacenterId.Value != Guid.Empty)
        {
            serverQuery = serverQuery.Where(s => s.DatacenterId == datacenterId.Value);
        }

        var servers = await serverQuery.ToListAsync();

        var connections = await _context.AppDependencies
            .AsNoTracking()
            .Select(ad => new ConnectionDto
            {
                SourceAppId = ad.SourceAppId,
                TargetAppId = ad.DestAppId
            })
            .ToListAsync();

        return new DependencyMapDto
        {
            Servers = servers.Select(s => new ServerNodeDto
            {
                Id = s.Id,
                Hostname = s.Hostname,
                IpAddress = s.IpAddress,
                Applications = s.PortMappings.Select(pm => new ApplicationNodeDto
                {
                    Id = pm.Application!.Id,
                    Name = pm.Application.AppName,
                    Port = pm.PortNumber,
                    Protocol = pm.Protocol
                }).ToList()
            }).ToList(),
            Connections = connections
        };
    }
}

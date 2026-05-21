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
                .ThenInclude(s => s.Applications)
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
                Applications = s.Applications.Select(a => new ApplicationNodeDto
                {
                    Id = a.Id,
                    Name = a.AppName,
                    Port = a.PortNumber,
                    Protocol = a.Protocol,
                    RiskLevel = a.RiskLevel.ToString()
                }).ToList()
            }).ToList()
        });
    }

    public async Task<DependencyMapDto> GetDependencyMapAsync()
    {
        var servers = await _context.Servers
            .Include(s => s.Applications)
            .AsNoTracking()
            .ToListAsync();

        var connections = await _context.Applications
            .Where(a => a.TargetApplicationId != null)
            .AsNoTracking()
            .Select(a => new ConnectionDto
            {
                SourceAppId = a.Id,
                TargetAppId = a.TargetApplicationId!.Value
            })
            .ToListAsync();

        return new DependencyMapDto
        {
            Servers = servers.Select(s => new ServerNodeDto
            {
                Id = s.Id,
                Hostname = s.Hostname,
                IpAddress = s.IpAddress,
                Applications = s.Applications.Select(a => new ApplicationNodeDto
                {
                    Id = a.Id,
                    Name = a.AppName,
                    Port = a.PortNumber,
                    Protocol = a.Protocol,
                    RiskLevel = a.RiskLevel.ToString()
                }).ToList()
            }).ToList(),
            Connections = connections
        };
    }
}

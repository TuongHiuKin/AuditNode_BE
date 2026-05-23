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

    public async Task<IEnumerable<ServerTopologyDto>> GetTopologyTreeAsync(Guid? datacenterId, int skip, int take)
    {
        var query = _context.Servers
            .Include(s => s.Applications)
            .AsNoTracking();

        if (datacenterId.HasValue)
        {
            query = query.Where(s => s.DatacenterId == datacenterId.Value);
        }

        var servers = await query
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return servers.Select(s => new ServerTopologyDto
        {
            Id = s.Id,
            Hostname = s.Hostname,
            IpAddress = s.IpAddress,
            Environment = s.Environment,
            Datacenter = s.Datacenter,
            Ports = s.Applications.Select(a => new PortTopologyDto
            {
                PortNumber = a.PortNumber,
                Protocol = a.Protocol,
                AppName = a.AppName,
                AppCode = a.AppCode
            }).ToList()
        });
    }

    public async Task<DependencyMapDto> GetDependencyMapAsync()
    {
        var applications = await _context.Applications
            .AsNoTracking()
            .ToListAsync();

        var nodes = applications.Select(a => new NodeDto
        {
            Id = a.Id.ToString(),
            Data = new NodeDataDto
            {
                Label = a.AppName,
                AppCode = a.AppCode,
                Risk = a.Risk
            },
            Position = new PositionDto { X = 0, Y = 0 } // Default position
        }).ToList();

        var edges = await _context.Applications
            .Where(a => a.TargetApplicationId != null)
            .AsNoTracking()
            .Select(a => new EdgeDto
            {
                Id = $"e-{a.Id}-{a.TargetApplicationId}",
                Source = a.Id.ToString(),
                Target = a.TargetApplicationId!.Value.ToString(),
                Label = a.Protocol
            })
            .ToListAsync();

        return new DependencyMapDto
        {
            Nodes = nodes,
            Edges = edges
        };
    }
}

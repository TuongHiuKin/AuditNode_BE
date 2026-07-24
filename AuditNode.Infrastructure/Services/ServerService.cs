using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using AuditNode.Infrastructure.Data;

namespace AuditNode.Infrastructure.Services;

public class ServerService : IServerService
{
    private readonly AuditDbContext _context;

    public ServerService(AuditDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<ServerResponseDto>> GetServersAsync()
    {
        var servers = await _context.Servers
            .Include(s => s.Datacenter)
            .Include(s => s.Labels)
            .ToListAsync();

        return servers.Select(s => new ServerResponseDto
        {
            Id = s.Id,
            DatacenterId = s.DatacenterId,
            IpAddress = s.IpAddress,
            Hostname = s.Hostname,
            OsType = s.OsType,
            Environment = s.Environment,
            Datacenter = s.DatacenterId == Guid.Empty ? "Unassigned" : (s.Datacenter?.Name ?? "Unassigned"),
            Status = s.Status,
            Labels = s.Labels != null ? s.Labels.Select(l => new LabelDto { Key = l.Key, Value = l.Value }).ToList() : new List<LabelDto>()
        });
    }

    public async Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids)
    {
        var servers = await _context.Servers
            .Include(s => s.Datacenter)
            .Include(s => s.Labels)
            .Where(s => ids.Contains(s.Id))
            .ToListAsync();

        return servers.Select(s => new ServerResponseDto
        {
            Id = s.Id,
            DatacenterId = s.DatacenterId,
            IpAddress = s.IpAddress,
            Hostname = s.Hostname,
            OsType = s.OsType,
            Environment = s.Environment,
            Datacenter = s.DatacenterId == Guid.Empty ? "Unassigned" : (s.Datacenter?.Name ?? "Unassigned"),
            Status = s.Status,
            Labels = s.Labels != null ? s.Labels.Select(l => new LabelDto { Key = l.Key, Value = l.Value }).ToList() : new List<LabelDto>()
        });
    }
}

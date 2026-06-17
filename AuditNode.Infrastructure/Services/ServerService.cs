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
        var servers = await _context.Servers.ToListAsync();

        return servers.Select(s => new ServerResponseDto
        {
            Id = s.Id,
            IpAddress = s.IpAddress,
            Hostname = s.Hostname,
            OsType = s.OsType,
            Environment = s.Environment,
            Status = s.Status
        });
    }

    public async Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids)
    {
        var servers = await _context.Servers
            .Where(s => ids.Contains(s.Id))
            .ToListAsync();

        return servers.Select(s => new ServerResponseDto
        {
            Id = s.Id,
            IpAddress = s.IpAddress,
            Hostname = s.Hostname,
            OsType = s.OsType,
            Environment = s.Environment,
            Status = s.Status
        });
    }
}

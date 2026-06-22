using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using AuditNode.Infrastructure.Data;
using System.Linq;

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
            .Include(s => s.PortMappings)
            .ThenInclude(pm => pm.Application)
            .ToListAsync();

        return servers.Select(s => new ServerResponseDto
        {
            Id = s.Id,
            DatacenterId = s.DatacenterId,
            IpAddress = s.IpAddress,
            Hostname = s.Hostname,
            OsType = s.OsType,
            Environment = s.Environment,
            Datacenter = s.Datacenter?.Name ?? string.Empty,
            Status = s.Status,
            Applications = s.PortMappings?.Select(pm => new ApplicationOnServerDto
            {
                Id = pm.Application!.Id,
                AppCode = pm.Application.AppCode,
                AppName = pm.Application.AppName,
                OwnerTeam = pm.Application.OwnerTeam,
                PortNumber = pm.PortNumber,
                Protocol = pm.Protocol
            }).ToList() ?? new List<ApplicationOnServerDto>()
        });
    }

    public async Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids)
    {
        var servers = await _context.Servers
            .Include(s => s.Datacenter)
            .Include(s => s.PortMappings)
            .ThenInclude(pm => pm.Application)
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
            Datacenter = s.Datacenter?.Name ?? string.Empty,
            Status = s.Status,
            Applications = s.PortMappings?.Select(pm => new ApplicationOnServerDto
            {
                Id = pm.Application!.Id,
                AppCode = pm.Application.AppCode,
                AppName = pm.Application.AppName,
                OwnerTeam = pm.Application.OwnerTeam,
                PortNumber = pm.PortNumber,
                Protocol = pm.Protocol
            }).ToList() ?? new List<ApplicationOnServerDto>()
        });
    }

    public async Task<ServerResponseDto?> GetServerByIdAsync(Guid id)
    {
        Console.WriteLine($"[DEBUG TRACE] Service querying Database for Server ID: {id}");

        var server = await _context.Servers
            .Include(s => s.Datacenter)
            .Include(s => s.PortMappings)
            .ThenInclude(pm => pm.Application)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (server != null)
        {
            Console.WriteLine("[DEBUG TRACE] Server FOUND in database.");
            return new ServerResponseDto
            {
                Id = server.Id,
                DatacenterId = server.DatacenterId,
                IpAddress = server.IpAddress,
                Hostname = server.Hostname,
                OsType = server.OsType,
                Environment = server.Environment,
                Datacenter = server.Datacenter?.Name ?? string.Empty,
                Status = server.Status,
                Applications = server.PortMappings?.Select(pm => new ApplicationOnServerDto
                {
                    Id = pm.Application!.Id,
                    AppCode = pm.Application.AppCode,
                    AppName = pm.Application.AppName,
                    OwnerTeam = pm.Application.OwnerTeam,
                    PortNumber = pm.PortNumber,
                    Protocol = pm.Protocol
                }).ToList() ?? new List<ApplicationOnServerDto>()
            };
        }

        Console.WriteLine("[DEBUG TRACE] Server NOT FOUND. It either doesn't exist or belongs to a different Workspace.");
        return null;
    }

    public async Task<bool> UpdateServerAsync(Guid id, UpdateServerDto updateDto)
    {
        var server = await _context.Servers.FirstOrDefaultAsync(s => s.Id == id);
        if (server == null)
        {
            return false;
        }

        server.Hostname = updateDto.Hostname;
        server.OsType = updateDto.OsType;
        server.Environment = updateDto.Environment;
        server.Status = updateDto.Status;
        if (updateDto.DatacenterId != Guid.Empty)
        {
            server.DatacenterId = updateDto.DatacenterId;
        }

        _context.Servers.Update(server);
        await _context.SaveChangesAsync();

        return true;
    }
}

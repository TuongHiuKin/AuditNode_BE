using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using AuditNode.Infrastructure.Data;
using AuditNode.Domain.Entities;
using System.Linq;

namespace AuditNode.Infrastructure.Services;

public class ServerService : IServerService
{
    private readonly AuditDbContext _context;

    public ServerService(AuditDbContext context)
    {
        _context = context;
    }

    private async Task<List<Label>> ProcessLabelsAsync(IEnumerable<LabelDto> incomingLabels)
    {
        var processedLabels = new List<Label>();
        if (incomingLabels == null || !incomingLabels.Any()) return processedLabels;

        foreach (var labelDto in incomingLabels)
        {
            var existingLabel = await _context.Labels
                .FirstOrDefaultAsync(l => l.Key == labelDto.Key && l.Value == labelDto.Value);

            if (existingLabel != null)
            {
                processedLabels.Add(existingLabel);
            }
            else
            {
                var newLabel = new Label 
                { 
                    Id = Guid.NewGuid(), 
                    Key = labelDto.Key, 
                    Value = labelDto.Value, 
                    ColorHex = labelDto.ColorHex ?? string.Empty 
                };
                processedLabels.Add(newLabel);
            }
        }
        return processedLabels;
    }

    public async Task<ServerResponseDto> CreateServerAsync(CreateServerDto createDto)
    {
        var server = new Server
        {
            Id = Guid.NewGuid(),
            DatacenterId = createDto.DatacenterId,
            IpAddress = createDto.IpAddress,
            Hostname = createDto.Hostname,
            OsType = createDto.OsType,
            Environment = createDto.Environment,
            Status = createDto.Status,
            Labels = await ProcessLabelsAsync(createDto.Labels)
        };

        _context.Servers.Add(server);
        await _context.SaveChangesAsync();

        return await GetServerByIdAsync(server.Id) ?? new ServerResponseDto();
    }

    public async Task<bool> UpdateServerAsync(Guid id, UpdateServerDto updateDto)
    {
        var server = await _context.Servers
            .Include(s => s.Labels)
            .FirstOrDefaultAsync(s => s.Id == id);
            
        if (server == null) return false;

        server.Hostname = updateDto.Hostname;
        server.OsType = updateDto.OsType;
        server.Environment = updateDto.Environment;
        server.Status = updateDto.Status;
        if (updateDto.DatacenterId != Guid.Empty)
        {
            server.DatacenterId = updateDto.DatacenterId;
        }

        var incomingLabels = updateDto.Labels ?? new List<LabelDto>();
        var actualLabels = new List<Label>();
        
        if (incomingLabels.Any())
        {
            var keys = incomingLabels.Select(l => l.Key).Distinct().ToList();
            var values = incomingLabels.Select(l => l.Value).Distinct().ToList();

            var candidateLabels = await _context.Labels
                .Where(l => keys.Contains(l.Key) && values.Contains(l.Value))
                .ToListAsync();

            foreach (var labelDto in incomingLabels)
            {
                var existing = candidateLabels.FirstOrDefault(l => l.Key == labelDto.Key && l.Value == labelDto.Value);
                if (existing != null)
                {
                    actualLabels.Add(existing);
                }
                else
                {
                    var newLabel = new Label
                    {
                        Id = Guid.NewGuid(),
                        Key = labelDto.Key,
                        Value = labelDto.Value,
                        ColorHex = string.IsNullOrWhiteSpace(labelDto.ColorHex) ? "#808080" : labelDto.ColorHex,
                        OwnerId = server.OwnerId
                    };
                    _context.Labels.Add(newLabel);
                    actualLabels.Add(newLabel);
                }
            }
        }
        
        var oldLabels = server.Labels.ToList();
        server.Labels.Clear();
        foreach (var label in actualLabels)
        {
            server.Labels.Add(label);
        }

        await _context.SaveChangesAsync();
        
        // Orphaned labels cleanup
        var removedLabels = oldLabels.Where(ol => !actualLabels.Any(al => al.Id == ol.Id)).ToList();
        if (removedLabels.Any())
        {
            foreach(var rl in removedLabels)
            {
                bool isUsedByServer = await _context.Servers.AnyAsync(s => s.Labels.Any(l => l.Id == rl.Id));
                bool isUsedByApp = await _context.Applications.AnyAsync(a => a.Labels.Any(l => l.Id == rl.Id));
                if (!isUsedByServer && !isUsedByApp)
                {
                    _context.Labels.Remove(rl);
                }
            }
            await _context.SaveChangesAsync();
        }

        return true;
    }

    public async Task<IEnumerable<ServerResponseDto>> GetServersAsync(string[]? labels = null, Guid? datacenterId = null)
    {
        var query = _context.Servers
            .Include(s => s.Datacenter)
            .Include(s => s.Labels)
            .Include(s => s.PortMappings)
            .ThenInclude(pm => pm.Application)
            .AsSplitQuery()
            .AsQueryable();

        if (datacenterId.HasValue)
        {
            query = query.Where(s => s.DatacenterId == datacenterId.Value);
        }

        if (labels != null && labels.Length > 0)
        {
            query = query.Where(s => s.Labels.Any(l => labels.Contains(l.Key) || labels.Contains(l.Value)));
        }

        var servers = await query.ToListAsync();

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
            Labels = s.Labels?.Select(l => new LabelDto { Key = l.Key, Value = l.Value, ColorHex = l.ColorHex }).ToList() ?? new List<LabelDto>(),
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
            .AsSplitQuery()
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
        var server = await _context.Servers
            .Include(s => s.Datacenter)
            .Include(s => s.Labels)
            .Include(s => s.PortMappings)
            .ThenInclude(pm => pm.Application)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (server != null)
        {
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
                Labels = server.Labels?.Select(l => new LabelDto { Key = l.Key, Value = l.Value, ColorHex = l.ColorHex }).ToList() ?? new List<LabelDto>(),
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

        return null;
    }
}

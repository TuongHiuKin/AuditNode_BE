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
            .Include(s => s.Labels)
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
            .Include(s => s.Labels)
            .Include(s => s.PortMappings)
            .ThenInclude(pm => pm.Application)
            .Where(s => ids.Contains(s.Id));

        return await MapToResponseDto(query).ToListAsync();
    }

    public async Task<IEnumerable<ServerResponseDto>> GetScopedAsync(IReadOnlySet<Guid>? serverIds, IReadOnlySet<Guid>? applicationIds, IEnumerable<Guid>? requestedIds = null)
    {
        var query = _dbContext.Servers.AsQueryable();
        if (serverIds is not null) query = query.Where(server => serverIds.Contains(server.Id));
        if (requestedIds is not null)
        {
            var ids = requestedIds.Where(id => id != Guid.Empty).Distinct().ToArray();
            query = query.Where(server => ids.Contains(server.Id));
        }
        return await MapToResponseDto(query, applicationIds).ToListAsync();
    }

    private IQueryable<ServerResponseDto> MapToResponseDto(IQueryable<Server> query, IReadOnlySet<Guid>? allowedApplicationIds = null)
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
            Applications = s.PortMappings.Where(pm => allowedApplicationIds == null || allowedApplicationIds.Contains(pm.AppId)).Select(pm => new ApplicationOnServerDto
            {
                PortMappingId = pm.Id,
                Id = pm.Application!.Id,
                AppCode = pm.Application.AppCode,
                AppName = pm.Application.AppName,
                OwnerTeam = pm.Application.OwnerTeam,
                PortNumber = pm.PortNumber,
                Protocol = pm.Protocol
            }).ToList(),
            Labels = s.Labels.Select(label => new LabelDto
            {
                Key = label.Key,
                Value = label.Value
            }).ToList()
        });
    }

    public async Task<Server?> GetByIdAsync(Guid id)
    {
        return await _dbContext.Servers
            .Include(s => s.Datacenter)
            .Include(s => s.Labels)
            .Include(s => s.PortMappings)
            .ThenInclude(pm => pm.Application)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public Task<bool> DatacenterExistsAsync(Guid id)
    {
        return _dbContext.Datacenters.AnyAsync(datacenter => datacenter.Id == id);
    }

    public Task<bool> IpAddressExistsAsync(string ipAddress, Guid? excludeServerId = null)
    {
        return _dbContext.Servers.AnyAsync(server =>
            server.IpAddress == ipAddress &&
            (!excludeServerId.HasValue || server.Id != excludeServerId.Value));
    }

    public async Task UpdateAsync(Server server, IReadOnlyCollection<LabelDto>? labels)
    {
        _dbContext.Entry(server).State = EntityState.Modified;
        if (labels is not null)
            await ReplaceLabelsAsync(server, labels);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<Server> CreateServerAsync(Server server, IReadOnlyCollection<LabelDto> labels)
    {
        _dbContext.Servers.Add(server);
        await ReplaceLabelsAsync(server, labels);
        await _dbContext.SaveChangesAsync();
        return server;
    }

    public async Task DeleteAsync(Server server)
    {
        _dbContext.Servers.Remove(server);
        await _dbContext.SaveChangesAsync();
    }

    private async Task ReplaceLabelsAsync(Server server, IReadOnlyCollection<LabelDto> labels)
    {
        var currentLinks = await _dbContext.ServerLabels
            .Where(link => link.ServerId == server.Id)
            .Include(link => link.Label)
            .ToListAsync();
        var normalized = labels
            .Select(label => new LabelDto { Key = label.Key.Trim(), Value = label.Value.Trim() })
            .GroupBy(label => new
            {
                Key = label.Key.ToUpperInvariant(),
                Value = label.Value.ToUpperInvariant()
            })
            .Select(group => group.First())
            .ToArray();
        var desiredIds = new HashSet<Guid>();

        foreach (var value in normalized)
        {
            var normalizedKey = value.Key.ToUpperInvariant();
            var normalizedValue = value.Value.ToUpperInvariant();
            var label = await _dbContext.Labels.FirstOrDefaultAsync(existing =>
                existing.OwnerUserId == server.OwnerUserId &&
                existing.Key.ToUpper() == normalizedKey && existing.Value.ToUpper() == normalizedValue);
            if (label is null)
            {
                label = new Label
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = server.WorkspaceId,
                    OwnerUserId = server.OwnerUserId,
                    Key = value.Key,
                    Value = value.Value
                };
                _dbContext.Labels.Add(label);
            }

            desiredIds.Add(label.Id);
            if (currentLinks.All(link => link.LabelId != label.Id))
            {
                _dbContext.ServerLabels.Add(new ServerLabel
                {
                    WorkspaceId = server.WorkspaceId,
                    OwnerUserId = server.OwnerUserId,
                    ServerId = server.Id,
                    LabelId = label.Id,
                    Server = server,
                    Label = label
                });
            }
        }

        _dbContext.ServerLabels.RemoveRange(currentLinks.Where(link => !desiredIds.Contains(link.LabelId)));
    }
}

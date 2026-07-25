using AuditNode.Domain.Entities;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace AuditNode.Infrastructure.Repositories;

public class TopologyRepository : ITopologyRepository
{
    private readonly AuditDbContext _context;

    public TopologyRepository(AuditDbContext context)
    {
        _context = context;
    }

    public async Task SaveTopologyStateAsync(SaveTopologyStateDto state)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Get existing nodes in this workspace
            var existingNodes = await _context.TopologyNodes.ToDictionaryAsync(n => n.Id);

            // 2. Process incoming nodes (Upsert)
            foreach (var nodeDto in state.Nodes)
            {
                if (existingNodes.TryGetValue(nodeDto.Id, out var existingNode))
                {
                    existingNode.NodeType = nodeDto.NodeType;
                    existingNode.Label = nodeDto.Label;
                    existingNode.X = nodeDto.X;
                    existingNode.Y = nodeDto.Y;
                    existingNode.Width = nodeDto.Width;
                    existingNode.Height = nodeDto.Height;
                    existingNode.ParentNodeId = nodeDto.ParentNodeId;
                    existingNode.ReferenceId = nodeDto.ReferenceId;
                    _context.TopologyNodes.Update(existingNode);
                }
                else
                {
                    var newNode = new TopologyNode
                    {
                        Id = nodeDto.Id,
                        NodeType = nodeDto.NodeType,
                        Label = nodeDto.Label,
                        X = nodeDto.X,
                        Y = nodeDto.Y,
                        Width = nodeDto.Width,
                        Height = nodeDto.Height,
                        ParentNodeId = nodeDto.ParentNodeId,
                        ReferenceId = nodeDto.ReferenceId
                    };
                    _context.TopologyNodes.Add(newNode);
                }
            }

            // 3. Delete nodes not in the incoming payload (if business rules require full sync)
            var incomingIds = state.Nodes.Select(n => n.Id).ToHashSet();
            var nodesToDelete = existingNodes.Values.Where(n => !incomingIds.Contains(n.Id));
            _context.TopologyNodes.RemoveRange(nodesToDelete);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IEnumerable<TopologyTreeDto>> GetTopologyTreeAsync(Guid? datacenterId = null, int skip = 0, int take = 100, List<string>? labels = null)
    {
        var query = _context.Datacenters
            .Include(d => d.Servers)
                .ThenInclude(s => s.PortMappings)
                    .ThenInclude(pm => pm.Application)
            .Include(d => d.Servers)
                .ThenInclude(s => s.Labels)
            .AsSplitQuery()
            .AsNoTracking();

        if (datacenterId.HasValue && datacenterId.Value != Guid.Empty)
        {
            query = query.Where(d => d.Id == datacenterId.Value);
        }

        if (labels != null && labels.Any())
        {
            query = query.Where(d => d.Servers.Any(s => s.Labels.Any(l =>
                labels.Contains(l.Key) || labels.Contains(l.Value) || labels.Contains(l.Key + ":" + l.Value))));
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
            Servers = d.Servers
                .Where(s => labels == null || !labels.Any() || s.Labels.Any(l =>
                    labels.Contains(l.Key) || labels.Contains(l.Value) || labels.Contains(l.Key + ":" + l.Value)))
                .Select(s => new ServerNodeDto
                {
                    Id = s.Id,
                    Hostname = s.Hostname,
                    IpAddress = s.IpAddress,
                    Labels = s.Labels.Select(l => new LabelDto
                    {
                        Key = l.Key,
                        Value = l.Value
                    }).ToList(),
                    Applications = s.PortMappings.Select(pm => new ApplicationNodeDto
                    {
                        Id = pm.Application!.Id,
                        Name = pm.Application.AppName,
                        PortMappingId = pm.Id,
                        Port = pm.PortNumber,
                        Protocol = pm.Protocol
                    }).ToList()
                }).ToList()
        });
    }

    public async Task<DependencyMapDto> GetDependencyMapAsync(string? environment = null, Guid? datacenterId = null, List<string>? labels = null)
    {
        var serverQuery = _context.Servers
            .Include(s => s.PortMappings)
                .ThenInclude(pm => pm.Application)
            .Include(s => s.Labels)
            .AsSplitQuery()
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(environment))
        {
            serverQuery = serverQuery.Where(s => s.Environment == environment);
        }

        if (datacenterId.HasValue && datacenterId.Value != Guid.Empty)
        {
            serverQuery = serverQuery.Where(s => s.DatacenterId == datacenterId.Value);
        }

        if (labels != null && labels.Any())
        {
            serverQuery = serverQuery.Where(s => s.Labels.Any(l =>
                labels.Contains(l.Key) || labels.Contains(l.Value) || labels.Contains(l.Key + ":" + l.Value)));
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
                Labels = s.Labels.Select(l => new LabelDto
                {
                    Key = l.Key,
                    Value = l.Value
                }).ToList(),
                Applications = s.PortMappings.Select(pm => new ApplicationNodeDto
                {
                    Id = pm.Application!.Id,
                    Name = pm.Application.AppName,
                    PortMappingId = pm.Id,
                    Port = pm.PortNumber,
                    Protocol = pm.Protocol
                }).ToList()
            }).ToList(),
            Connections = connections
        };
    }

    public async Task<IEnumerable<ApplicationStatusDto>> GetApplicationStatusAsync()
    {
        var mappedAppIds = await _context.AppDependencies
            .AsNoTracking()
            .Select(ad => ad.SourceAppId)
            .Union(_context.AppDependencies.AsNoTracking().Select(ad => ad.DestAppId))
            .Distinct()
            .ToListAsync();

        return await _context.Applications
            .AsNoTracking()
            .Select(a => new ApplicationStatusDto
            {
                Id = a.Id,
                AppName = a.AppName,
                IsMapped = mappedAppIds.Contains(a.Id)
            })
            .ToListAsync();
    }
}

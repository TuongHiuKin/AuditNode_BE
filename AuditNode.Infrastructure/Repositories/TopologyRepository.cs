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
                    PortMappingId = pm.Id,
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

    public async Task SyncTopologyAsync(TopologySyncRequestDto request, string ownerId)
    {
        if (!Guid.TryParse(ownerId, out var parsedOwnerId))
        {
            throw new UnauthorizedAccessException("Invalid Owner ID.");
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var idMapping = new Dictionary<string, Guid>();

            // Process Frames
            foreach (var frameDto in request.Frames)
            {
                if (Guid.TryParse(frameDto.Id, out var existingGuid))
                {
                    // Existing frame
                    var frame = await _context.BoundaryFrames.FirstOrDefaultAsync(f => f.Id == existingGuid && f.OwnerId == parsedOwnerId);
                    if (frame != null)
                    {
                        frame.Name = frameDto.Name;
                        frame.XPosition = frameDto.X;
                        frame.YPosition = frameDto.Y;
                        frame.Width = frameDto.Width;
                        frame.Height = frameDto.Height;
                    }
                    idMapping[frameDto.Id] = existingGuid;
                }
                else
                {
                    // New frame from temp ID
                    var newGuid = Guid.NewGuid();
                    var newFrame = new BoundaryFrame
                    {
                        Id = newGuid,
                        Name = frameDto.Name,
                        XPosition = frameDto.X,
                        YPosition = frameDto.Y,
                        Width = frameDto.Width,
                        Height = frameDto.Height,
                        OwnerId = parsedOwnerId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.BoundaryFrames.Add(newFrame);
                    idMapping[frameDto.Id] = newGuid;
                }
            }

            // Process Assignments
            foreach (var assignment in request.Assignments)
            {
                Guid? targetFrameId = null;
                if (!string.IsNullOrEmpty(assignment.ParentFrameId))
                {
                    if (idMapping.TryGetValue(assignment.ParentFrameId, out var mappedId))
                    {
                        targetFrameId = mappedId;
                    }
                    else if (Guid.TryParse(assignment.ParentFrameId, out var parsedGuid))
                    {
                        targetFrameId = parsedGuid;
                    }
                }

                // Check Server
                var server = await _context.Servers.FirstOrDefaultAsync(s => s.Id == assignment.NodeId && s.OwnerId == ownerId);
                if (server != null)
                {
                    server.ParentFrameId = targetFrameId;
                    continue;
                }

                // Check Application
                var app = await _context.Applications.FirstOrDefaultAsync(a => a.Id == assignment.NodeId && a.OwnerId == ownerId);
                if (app != null)
                {
                    app.ParentFrameId = targetFrameId;
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

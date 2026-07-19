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
    private readonly ICurrentUserService _currentUserService;

    public TopologyRepository(AuditDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    private string GetCurrentUserId()
    {
        return _currentUserService.UserId
            ?? throw new UnauthorizedAccessException("User ID not found in the current request.");
    }

    private static ServerNodeDto MapServerNode(Server server, string currentUserId)
    {
        return new ServerNodeDto
        {
            Id = server.Id,
            Hostname = server.Hostname,
            IpAddress = server.IpAddress,
            OsType = server.OsType,
            Environment = server.Environment,
            Status = server.Status,
            Applications = server.PortMappings
                .Where(mapping => mapping.Application?.OwnerId == currentUserId)
                .Select(pm => new ApplicationNodeDto
                {
                    Id = pm.Application!.Id,
                    Name = pm.Application.AppName,
                    PortMappingId = pm.Id,
                    Port = pm.PortNumber,
                    Protocol = pm.Protocol,
                    Labels = pm.Application.Labels.Select(label => new TopologyLabelDto
                    {
                        Id = label.Id,
                        Key = label.Key,
                        Value = label.Value,
                        ColorHex = label.ColorHex
                    }).ToList()
                }).ToList(),
            Labels = server.Labels.Select(label => new TopologyLabelDto
            {
                Id = label.Id,
                Key = label.Key,
                Value = label.Value,
                ColorHex = label.ColorHex
            }).ToList()
        };
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
        var currentUserId = GetCurrentUserId();
        var query = _context.Datacenters
            .Include(d => d.Servers)
                .ThenInclude(s => s.Labels)
            .Include(d => d.Servers)
                .ThenInclude(s => s.PortMappings)
                    .ThenInclude(pm => pm.Application)
                        .ThenInclude(application => application!.Labels)
            .Where(d => d.OwnerId == currentUserId)
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
            Servers = d.Servers
                .Select(server => MapServerNode(server, currentUserId))
                .ToList()
        });
    }

    public async Task<DependencyMapDto> GetDependencyMapAsync(Guid[]? labelIds = null, string? environment = null, Guid? datacenterId = null)
    {
        var currentUserId = GetCurrentUserId();
        var serverQuery = _context.Servers
            .Include(s => s.Labels)
            .Include(s => s.PortMappings)
                .ThenInclude(pm => pm.Application)
                    .ThenInclude(application => application!.Labels)
            .Where(s => s.OwnerId == currentUserId)
            .AsNoTracking();

        if (labelIds != null && labelIds.Any())
        {
            serverQuery = serverQuery.Where(server =>
                server.Labels.Any(label => labelIds.Contains(label.Id)) ||
                server.PortMappings.Any(mapping =>
                    mapping.Application != null &&
                    mapping.Application.OwnerId == currentUserId &&
                    mapping.Application.Labels.Any(label => labelIds.Contains(label.Id))));
        }

        if (!string.IsNullOrWhiteSpace(environment))
        {
            serverQuery = serverQuery.Where(s => s.Environment == environment);
        }

        if (datacenterId.HasValue && datacenterId.Value != Guid.Empty)
        {
            serverQuery = serverQuery.Where(s => s.DatacenterId == datacenterId.Value);
        }

        var servers = await serverQuery.ToListAsync();
        var visibleAppIds = servers
            .SelectMany(server => server.PortMappings)
            .Select(mapping => mapping.AppId)
            .Distinct()
            .ToList();

        var connections = await _context.AppDependencies
            .Where(dependency =>
                visibleAppIds.Contains(dependency.SourceAppId) &&
                visibleAppIds.Contains(dependency.DestAppId))
            .AsNoTracking()
            .Select(ad => new ConnectionDto
            {
                SourceAppId = ad.SourceAppId,
                TargetAppId = ad.DestAppId
            })
            .ToListAsync();

        return new DependencyMapDto
        {
            Servers = servers
                .Select(server => MapServerNode(server, currentUserId))
                .ToList(),
            Connections = connections
        };
    }

    public async Task<IEnumerable<ServerNodeDto>> GetExternalDependenciesAsync(Guid id, Guid[]? labelIds = null)
    {
        var currentUserId = GetCurrentUserId();
        // Get applications on the target server
        var serverAppIds = await _context.PortMappings
            .Where(pm => pm.ServerId == id && pm.Server!.OwnerId == currentUserId)
            .Select(pm => pm.AppId)
            .ToListAsync();

        // Find connected application IDs
        var connectedAppIds = await _context.AppDependencies
            .Where(ad => serverAppIds.Contains(ad.SourceAppId))
            .Select(ad => ad.DestAppId)
            .Union(
                _context.AppDependencies
                .Where(ad => serverAppIds.Contains(ad.DestAppId))
                .Select(ad => ad.SourceAppId)
            )
            .Distinct()
            .ToListAsync();

        // Get servers hosting those connected apps
        IQueryable<Server> externalServersQuery = _context.Servers
            .Include(s => s.Labels)
            .Include(s => s.PortMappings)
                .ThenInclude(pm => pm.Application)
                    .ThenInclude(application => application!.Labels)
            .Where(s =>
                s.OwnerId == currentUserId &&
                s.Id != id &&
                s.PortMappings.Any(pm => connectedAppIds.Contains(pm.AppId)))
            .AsNoTracking();

        // Filter out those that share a label with the current filter
        if (labelIds != null && labelIds.Any())
        {
            externalServersQuery = externalServersQuery.Where(
                server => !server.Labels.Any(label => labelIds.Contains(label.Id)));
        }

        var externalServers = await externalServersQuery.ToListAsync();

        return externalServers.Select(server => MapServerNode(server, currentUserId));
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

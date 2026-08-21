using System.Net;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditNode.Infrastructure.Services;

public class ServerService : IServerService
{
    private readonly IServerRepository _repository;
    private readonly ITenantProvider _tenantProvider;

    public ServerService(IServerRepository repository, ITenantProvider tenantProvider)
    {
        _repository = repository;
        _tenantProvider = tenantProvider;
    }

    public async Task<IEnumerable<ServerResponseDto>> GetServersAsync()
    {
        if (!HasWorkspace())
            return Array.Empty<ServerResponseDto>();

        return await _repository.GetAllWithAppsAsync();
    }

    public async Task<ServerResponseDto?> GetServerAsync(Guid id)
    {
        if (!HasWorkspace() || id == Guid.Empty)
            return null;

        var server = await _repository.GetByIdAsync(id);
        return server is null ? null : Map(server);
    }

    public async Task<ServerOperationResult> CreateServerAsync(CreateServerDto dto)
    {
        if (!HasWorkspace())
            return new(ServerOperationStatus.InvalidWorkspace);

        if (!await _repository.DatacenterExistsAsync(dto.DatacenterId))
            return new(ServerOperationStatus.DatacenterNotFound);

        var normalizedIp = NormalizeIp(dto.IpAddress);
        if (await _repository.IpAddressExistsAsync(normalizedIp, null))
            return new(ServerOperationStatus.DuplicateIp);

        var server = new Server
        {
            Id = Guid.NewGuid(),
            DatacenterId = dto.DatacenterId,
            IpAddress = normalizedIp,
            Hostname = dto.Hostname,
            OsType = dto.OsType,
            Environment = dto.Environment,
            Status = dto.Status
        };

        try
        {
            await _repository.CreateServerAsync(server, dto.Labels);
            return new(ServerOperationStatus.Success, Map(server));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return new(ServerOperationStatus.DuplicateIp);
        }
    }

    public async Task<ServerOperationResult> UpdateServerAsync(Guid id, UpdateServerDto dto)
    {
        if (!HasWorkspace())
            return new(ServerOperationStatus.InvalidWorkspace);

        if (id == Guid.Empty)
            return new(ServerOperationStatus.NotFound);

        var server = await _repository.GetByIdAsync(id);
        if (server is null)
            return new(ServerOperationStatus.NotFound);

        if (!await _repository.DatacenterExistsAsync(dto.DatacenterId))
            return new(ServerOperationStatus.DatacenterNotFound);

        var normalizedIp = NormalizeIp(dto.IpAddress);
        if (await _repository.IpAddressExistsAsync(normalizedIp, id))
            return new(ServerOperationStatus.DuplicateIp);

        server.DatacenterId = dto.DatacenterId;
        server.IpAddress = normalizedIp;
        server.Hostname = dto.Hostname;
        server.OsType = dto.OsType;
        server.Environment = dto.Environment;
        server.Status = dto.Status;

        try
        {
            await _repository.UpdateAsync(server, dto.Labels);
            return new(ServerOperationStatus.Success, Map(server));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return new(ServerOperationStatus.DuplicateIp);
        }
    }

    public async Task<ServerOperationStatus> PurgeServerAsync(Guid id)
    {
        if (!HasWorkspace())
            return ServerOperationStatus.InvalidWorkspace;

        if (id == Guid.Empty)
            return ServerOperationStatus.NotFound;

        var server = await _repository.GetByIdAsync(id);
        if (server is null)
            return ServerOperationStatus.NotFound;

        await _repository.DeleteAsync(server);
        return ServerOperationStatus.Success;
    }

    public async Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids)
    {
        if (!HasWorkspace())
            return Array.Empty<ServerResponseDto>();

        return await _repository.GetByIdsAsync(ids.Where(id => id != Guid.Empty).Distinct());
    }

    private bool HasWorkspace() { Console.WriteLine("HasWorkspace called. WorkspaceId: " + _tenantProvider.WorkspaceId); return
        _tenantProvider.WorkspaceId.HasValue; }

    private static string NormalizeIp(string ipAddress) => IPAddress.Parse(ipAddress).ToString();

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };

    private static ServerResponseDto Map(Server server) => new()
    {
        Id = server.Id,
        DatacenterId = server.DatacenterId,
        IpAddress = server.IpAddress,
        Hostname = server.Hostname,
        OsType = server.OsType,
        Environment = server.Environment,
        Datacenter = server.Datacenter?.Name ?? string.Empty,
        Status = server.Status,
        Applications = server.PortMappings.Select(mapping => new ApplicationOnServerDto
        {
            PortMappingId = mapping.Id,
            Id = mapping.AppId,
            AppCode = mapping.Application?.AppCode ?? string.Empty,
            AppName = mapping.Application?.AppName ?? string.Empty,
            OwnerTeam = mapping.Application?.OwnerTeam ?? string.Empty,
            PortNumber = mapping.PortNumber,
            Protocol = mapping.Protocol
        }).ToList(),
        Labels = server.Labels.Select(label => new LabelDto
        {
            Key = label.Key,
            Value = label.Value
        }).ToList()
    };
}

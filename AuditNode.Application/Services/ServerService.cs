using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using System.Linq;

namespace AuditNode.Application.Services;

public class ServerService : IServerService
{
    private readonly IServerRepository _repository;

    public ServerService(IServerRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<ServerResponseDto>> GetAllAsync(string? environment = null, Guid? datacenterId = null)
    {
        return await _repository.GetAllWithAppsAsync(environment, datacenterId);
    }

    public async Task<IEnumerable<ServerResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        return await _repository.GetByIdsAsync(ids);
    }

    public async Task<ServerDetailDto?> GetByIdAsync(Guid id)
    {
        var server = await _repository.GetByIdAsync(id);
        if (server == null) return null;

        return new ServerDetailDto
        {
            Id = server.Id,
            DatacenterId = server.DatacenterId,
            DatacenterName = server.Datacenter?.Name ?? string.Empty,
            IpAddress = server.IpAddress,
            Hostname = server.Hostname,
            OsType = server.OsType,
            Environment = server.Environment,
            Status = server.Status,
            Applications = server.PortMappings.Select(pm => new ApplicationOnServerDto
            {
                Id = pm.Application!.Id,
                AppCode = pm.Application.AppCode,
                AppName = pm.Application.AppName,
                OwnerTeam = pm.Application.OwnerTeam,
                PortNumber = pm.PortNumber,
                Protocol = pm.Protocol
            }).ToList()
        };
    }

    public async Task<ServerResponseDto> CreateAsync(CreateServerDto serverDto)
    {
        var server = new Server
        {
            Id = Guid.NewGuid(),
            DatacenterId = serverDto.DatacenterId,
            IpAddress = serverDto.IpAddress,
            Hostname = serverDto.Hostname,
            OsType = serverDto.OsType,
            Environment = serverDto.Environment,
            Status = serverDto.Status
        };

        var createdServer = await _repository.CreateServerAsync(server);

        return new ServerResponseDto
        {
            Id = createdServer.Id,
            DatacenterId = createdServer.DatacenterId,
            IpAddress = createdServer.IpAddress,
            Hostname = createdServer.Hostname,
            OsType = createdServer.OsType,
            Environment = createdServer.Environment,
            Status = createdServer.Status
        };
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateServerDto updateDto)
    {
        var existingServer = await _repository.GetByIdAsync(id);
        if (existingServer == null) return false;

        existingServer.Hostname = updateDto.Hostname;
        existingServer.OsType = updateDto.OsType;
        existingServer.Environment = updateDto.Environment;
        existingServer.Status = updateDto.Status;
        existingServer.DatacenterId = updateDto.DatacenterId;

        await _repository.UpdateAsync(existingServer);
        return true;
    }
}

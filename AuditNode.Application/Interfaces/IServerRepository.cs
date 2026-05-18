using AuditNode.Domain.Entities;
using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IServerRepository
{
    Task<IEnumerable<ServerResponseDto>> GetAllWithAppsAsync(string? environment = null, Guid? datacenterId = null);
    Task<Server> CreateServerAsync(Server server);
}

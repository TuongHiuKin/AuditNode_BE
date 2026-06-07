using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IDependencyService
{
    Task SyncDependenciesAsync(SyncDependenciesDto syncDto);
}

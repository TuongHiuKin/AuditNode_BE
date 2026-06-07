using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IDependencyRepository
{
    Task SyncDependenciesAsync(SyncDependenciesDto syncDto);
}

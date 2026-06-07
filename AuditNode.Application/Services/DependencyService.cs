using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;

namespace AuditNode.Application.Services;

public class DependencyService : IDependencyService
{
    private readonly IDependencyRepository _repository;

    public DependencyService(IDependencyRepository repository)
    {
        _repository = repository;
    }

    public async Task SyncDependenciesAsync(SyncDependenciesDto syncDto)
    {
        await _repository.SyncDependenciesAsync(syncDto);
    }
}

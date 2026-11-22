using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using AuditNode.Application.DTOs;

namespace AuditNode.Infrastructure.Repositories;

public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly ICurrentUserService _currentUser;
    private readonly IGlobalCatalogRepository _catalog;
    private readonly TimeProvider _timeProvider;

    public AnalyticsRepository(ICurrentUserService currentUser, IGlobalCatalogRepository catalog, TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _catalog = catalog;
        _timeProvider = timeProvider;
    }

    public async Task<IEnumerable<TopologyView>> GetTopologyCatalogAsync(
        CatalogView view, string? environment = null, Guid? datacenterId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return [];
        return await _catalog.GetTopologyAnalyticsAsync(
            _currentUser.UserId!, view, _timeProvider.GetUtcNow().UtcDateTime, environment, datacenterId, cancellationToken);
    }

    public async Task<IEnumerable<DependencyView>> GetDependenciesCatalogAsync(
        CatalogView view, string? environment = null, Guid? datacenterId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return [];
        return await _catalog.GetDependencyAnalyticsAsync(
            _currentUser.UserId!, view, _timeProvider.GetUtcNow().UtcDateTime, environment, datacenterId, cancellationToken);
    }

    public Task<IEnumerable<TopologyView>> GetTopologyAsync(string? environment = null, Guid? datacenterId = null) =>
        GetTopologyCatalogAsync(CatalogView.Mine, environment, datacenterId).ContinueWith<IEnumerable<TopologyView>>(task => task.Result, TaskScheduler.Default);

    public Task<IEnumerable<DependencyView>> GetDependenciesAsync(string? environment = null, Guid? datacenterId = null) =>
        GetDependenciesCatalogAsync(CatalogView.Mine, environment, datacenterId).ContinueWith<IEnumerable<DependencyView>>(task => task.Result, TaskScheduler.Default);
}

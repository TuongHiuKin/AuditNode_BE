using System.Data;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AuditNode.Infrastructure.Services;

public class DependencyService : IDependencyService
{
    private readonly AuditDbContext _dbContext;
    private readonly ILogger<DependencyService> _logger;
    private readonly IWorkspaceAccessService _access;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantProvider _tenant;

    public DependencyService(
        AuditDbContext dbContext,
        ILogger<DependencyService> logger,
        IWorkspaceAccessService access,
        ICurrentUserService currentUser,
        ITenantProvider tenant)
    {
        _dbContext = dbContext;
        _logger = logger;
        _access = access;
        _currentUser = currentUser;
        _tenant = tenant;
    }

    public async Task<DependencySyncStatus> SyncDependenciesAsync(SyncDependenciesDto dto)
    {
        if (dto is null) return DependencySyncStatus.InvalidRequest;
        var payload = dto.Dependencies;
        if (payload is null || dto.Version is null || dto.Version < 0 || !_tenant.WorkspaceId.HasValue || _tenant.WorkspaceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(_currentUser.UserId))
            return DependencySyncStatus.InvalidRequest;
        var workspaceId = _tenant.WorkspaceId.Value;
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : null;
        var workspace = _dbContext.Database.IsRelational()
            ? await _dbContext.Workspaces.FromSqlInterpolated($"SELECT * FROM workspaces WHERE id = {workspaceId} FOR UPDATE").SingleOrDefaultAsync()
            : await _dbContext.Workspaces.SingleOrDefaultAsync(item => item.Id == workspaceId);
        if (workspace is null) return DependencySyncStatus.Forbidden;
        if (workspace.OwnerUserId != _currentUser.UserId)
        {
            if (_dbContext.Database.IsRelational())
            {
                _ = await _dbContext.WorkspaceMembers.FromSqlInterpolated(
                        $"SELECT * FROM workspace_members WHERE workspace_id = {workspaceId} AND user_id = {_currentUser.UserId!} FOR UPDATE")
                    .SingleOrDefaultAsync();
            }
            var access = await _access.ResolveAsync(workspaceId, _currentUser.UserId!);
            if (access?.EffectiveRole != WorkspaceRoles.Admin) return DependencySyncStatus.Forbidden;
        }
        if (workspace.TopologyVersion != dto.Version.Value) return DependencySyncStatus.Conflict;
        if (payload.Any(item =>
                item.SourceAppId == Guid.Empty || item.DestAppId == Guid.Empty || item.DestinationPortMappingId == Guid.Empty))
            return DependencySyncStatus.InvalidRequest;
        if (payload.Any(item => item.SourceAppId == item.DestAppId))
            return DependencySyncStatus.SelfLoop;

        var keys = payload.Select(Key).ToArray();
        if (keys.Distinct().Count() != keys.Length)
            return DependencySyncStatus.Duplicate;

        var appIds = payload.SelectMany(item => new[] { item.SourceAppId, item.DestAppId }).Distinct().ToArray();
        var visibleAppIds = await _dbContext.Applications.IgnoreQueryFilters()
            .Where(application => application.WorkspaceId == workspaceId && appIds.Contains(application.Id))
            .Select(application => application.Id)
            .ToListAsync();
        if (visibleAppIds.Count != appIds.Length)
            return DependencySyncStatus.NotFound;

        var destinationPortIds = payload.Select(item => item.DestinationPortMappingId).Distinct().ToArray();
        var destinationPorts = await _dbContext.PortMappings.IgnoreQueryFilters()
            .Where(mapping => mapping.WorkspaceId == workspaceId && destinationPortIds.Contains(mapping.Id))
            .Select(mapping => new { mapping.Id, mapping.AppId })
            .ToDictionaryAsync(mapping => mapping.Id, mapping => mapping.AppId);
        if (destinationPorts.Count != destinationPortIds.Length)
            return DependencySyncStatus.NotFound;
        if (payload.Any(item => destinationPorts[item.DestinationPortMappingId] != item.DestAppId))
            return DependencySyncStatus.DestinationMismatch;

        var existing = await _dbContext.AppDependencies.IgnoreQueryFilters()
            .Where(item => item.WorkspaceId == workspaceId).ToListAsync();
        var desiredKeys = keys.ToHashSet();
        var canonicalExisting = existing.GroupBy(Key).ToDictionary(group => group.Key, group => group.First());
        var toDelete = existing
            .Where(dependency => !desiredKeys.Contains(Key(dependency)) ||
                                 canonicalExisting[Key(dependency)].Id != dependency.Id)
            .ToList();
        var toInsert = payload
            .Where(item => !canonicalExisting.ContainsKey(Key(item)))
            .Select(item => new AppDependency
            {
                Id = Guid.NewGuid(),
                SourceAppId = item.SourceAppId,
                DestAppId = item.DestAppId,
                DestPortId = item.DestinationPortMappingId,
                ConnectionType = "Automatic",
                CreatedAt = DateTime.UtcNow
            }).ToList();
        var referencedIds = await _dbContext.TopologyEdges.IgnoreQueryFilters()
            .Where(edge => edge.WorkspaceId == workspaceId && edge.ReferenceId.HasValue)
            .Select(edge => edge.ReferenceId!.Value).ToListAsync();
        if (toDelete.Any(item => referencedIds.Contains(item.Id)))
            return DependencySyncStatus.InvalidRequest;
        try
        {
            _dbContext.AppDependencies.RemoveRange(toDelete);
            await _dbContext.AppDependencies.AddRangeAsync(toInsert);
            workspace.TopologyVersion++;
            await _dbContext.SaveChangesAsync();
            if (transaction is not null) await transaction.CommitAsync();
            return DependencySyncStatus.Success;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Dependency synchronization failed.");
            if (transaction is not null) await transaction.RollbackAsync();
            throw;
        }
    }

    private static string Key(DependencyItemDto item) =>
        $"{item.SourceAppId:N}|{item.DestAppId:N}|{item.DestinationPortMappingId:N}";

    private static string Key(AppDependency item) =>
        $"{item.SourceAppId:N}|{item.DestAppId:N}|{item.DestPortId:N}";
}

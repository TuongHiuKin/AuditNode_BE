using AuditNode.Application.DTOs;
using AuditNode.Domain.Entities;

namespace AuditNode.Application.Interfaces;

public interface IGlobalCatalogRepository
{
    Task<CursorPageDto<ServerResponseDto>> GetServersAsync(string userId, CatalogPageQuery query, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<CursorPageDto<ApplicationResponseDto>> GetApplicationsAsync(string userId, CatalogPageQuery query, DateTime utcNow, string? labelKey = null, string? labelValue = null, CancellationToken cancellationToken = default);
    Task<CursorPageDto<DatacenterDto>> GetDatacentersAsync(string userId, CatalogPageQuery query, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<CursorPageDto<CatalogLabelDto>> GetLabelsAsync(string userId, CatalogPageQuery query, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<CursorPageDto<SearchResultDto>> SearchAsync(string userId, string keyword, CatalogPageQuery query, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServerResponseDto>> ExportServersAsync(string userId, CatalogView view, IReadOnlyCollection<Guid> ids, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApplicationResponseDto>> ExportApplicationsAsync(string userId, CatalogView view, IReadOnlyCollection<Guid> ids, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<ServerResponseDto?> GetServerAsync(string userId, Guid id, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<ApplicationResponseDto?> GetApplicationAsync(string userId, Guid id, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<int?> GetDependencyCountAsync(string userId, Guid applicationId, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeployedAppDto>?> GetDeployedApplicationsAsync(string userId, Guid serverId, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<CursorPageDto<ShareCatalogItemDto>> BrowseShareAsync(ShareTokenResolutionDto scope, string resourceType, CatalogPageQuery query, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TopologyView>> GetTopologyAnalyticsAsync(string userId, CatalogView view, DateTime utcNow, string? environment = null, Guid? datacenterId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DependencyView>> GetDependencyAnalyticsAsync(string userId, CatalogView view, DateTime utcNow, string? environment = null, Guid? datacenterId = null, CancellationToken cancellationToken = default);
}

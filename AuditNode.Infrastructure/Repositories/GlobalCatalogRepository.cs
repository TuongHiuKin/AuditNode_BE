using AuditNode.Application.DTOs;
using AuditNode.Application.Exceptions;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Infrastructure.Repositories;

public sealed class GlobalCatalogRepository(
    AuditDbContext context,
    ICatalogCursorCodec cursors) : IGlobalCatalogRepository
{
    public async Task<IReadOnlyList<TopologyView>> GetTopologyAnalyticsAsync(
        string userId, CatalogView view, DateTime utcNow, string? environment = null, Guid? datacenterId = null, CancellationToken cancellationToken = default)
    {
        Validate(userId, new CatalogPageQuery(view, 1));
        var servers = AuthorizedServers(userId, view, utcNow);
        var applications = AuthorizedApplications(userId, view, utcNow);
        var query = context.TopologyViews.IgnoreQueryFilters().AsNoTracking().Where(row =>
            servers.Any(server => server.Id == row.ServerId) &&
            applications.Any(application => application.Id == row.AppId));
        if (!string.IsNullOrWhiteSpace(environment)) query = query.Where(row => row.Environment == environment);
        if (datacenterId.HasValue && datacenterId != Guid.Empty) query = query.Where(row => row.DatacenterId == datacenterId);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DependencyView>> GetDependencyAnalyticsAsync(
        string userId, CatalogView view, DateTime utcNow, string? environment = null, Guid? datacenterId = null, CancellationToken cancellationToken = default)
    {
        Validate(userId, new CatalogPageQuery(view, 1));
        var applications = AuthorizedApplications(userId, view, utcNow);
        var query = context.DependencyViews.IgnoreQueryFilters().AsNoTracking().Where(row =>
            applications.Any(application => application.Id == row.SourceAppId) &&
            applications.Any(application => application.Id == row.DestAppId));
        if (!string.IsNullOrWhiteSpace(environment)) query = query.Where(row => row.Environment == environment);
        if (datacenterId.HasValue && datacenterId != Guid.Empty) query = query.Where(row => row.DatacenterId == datacenterId);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<CursorPageDto<ShareCatalogItemDto>> BrowseShareAsync(
        ShareTokenResolutionDto scope,
        string resourceType,
        CatalogPageQuery query,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (scope.GrantId == Guid.Empty || scope.LabelId == Guid.Empty || string.IsNullOrWhiteSpace(scope.OwnerUserId) ||
            scope.Permission != LabelGrantPermissions.Viewer)
            return new CursorPageDto<ShareCatalogItemDto>([], null, false);
        Validate(scope.OwnerUserId, query);
        var type = resourceType.Trim().ToLowerInvariant();
        if (type is not "servers" and not "applications")
            throw new CatalogQueryValidationException("Share browse resourceType must be 'servers' or 'applications'.");
        var binding = $"share:{scope.GrantId:N}:{scope.LabelId:N}:{scope.OwnerUserId}";
        var fingerprint = CatalogFilterFingerprint.Search($"resource={type}");
        var endpoint = $"share-{type}";
        var cursor = Position(endpoint, query, binding, fingerprint, 1);
        var scopeActive = context.LabelGrants.IgnoreQueryFilters().AsNoTracking().Any(grant =>
            grant.Id == scope.GrantId && grant.LabelId == scope.LabelId && grant.OwnerUserId == scope.OwnerUserId &&
            grant.GranteeUserId == null && grant.TokenHash != null && grant.Permission == LabelGrantPermissions.Viewer &&
            grant.RevokedAt == null && grant.ExpiresAt != null && grant.ExpiresAt > utcNow &&
            grant.Label != null && grant.Label.OwnerUserId == scope.OwnerUserId);
        var label = await context.Labels.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == scope.LabelId && item.OwnerUserId == scope.OwnerUserId)
            .Select(item => new LabelDto { Key = item.Key, Value = item.Value })
            .SingleOrDefaultAsync(cancellationToken);
        if (label is null) return new CursorPageDto<ShareCatalogItemDto>([], null, false);

        if (type == "servers")
        {
            var resources = context.Servers.IgnoreQueryFilters().AsNoTracking().Where(server =>
                scopeActive && server.OwnerUserId == scope.OwnerUserId &&
                (scope.SharesAllOwnerResources || context.ServerLabels.IgnoreQueryFilters().Any(link =>
                    link.ServerId == server.Id && link.OwnerUserId == scope.OwnerUserId && link.LabelId == scope.LabelId)));
            if (cursor is not null)
            {
                var sort = cursor.SortValues[0];
                var id = cursor.Id;
                resources = resources.Where(server => server.Hostname.CompareTo(sort) > 0 ||
                    (server.Hostname == sort && server.Id.CompareTo(id) > 0));
            }
            var rows = await resources.OrderBy(server => server.Hostname).ThenBy(server => server.Id)
                .Select(server => new ServerRow(server.Id, server.OwnerUserId!, server.DatacenterId, server.IpAddress,
                    server.Hostname, server.OsType, server.Environment, server.Datacenter != null ? server.Datacenter.Name : string.Empty, server.Status))
                .Take(query.Limit + 1).ToListAsync(cancellationToken);
            var hasNext = rows.Count > query.Limit;
            if (hasNext) rows.RemoveAt(rows.Count - 1);
            var items = rows.Select(row => new ShareCatalogItemDto
            {
                Type = "SERVER",
                Server = new ServerResponseDto
                {
                    Id = row.Id, OwnerUserId = row.OwnerUserId, DatacenterId = row.DatacenterId, IpAddress = row.IpAddress,
                    Hostname = row.Hostname, OsType = row.OsType, Environment = row.Environment, Datacenter = row.Datacenter,
                    Status = row.Status, Labels = [label], EffectivePermission = LabelEffectivePermission.Viewer,
                    SharedLabelIds = [scope.LabelId], Capabilities = CatalogCapabilities.Viewer
                }
            }).ToList();
            return Page(items, hasNext, endpoint, query, binding, fingerprint, item => [item.Server!.Hostname], item => item.Server!.Id);
        }

        var applications = context.Applications.IgnoreQueryFilters().AsNoTracking().Where(application =>
            scopeActive && application.OwnerUserId == scope.OwnerUserId &&
            (scope.SharesAllOwnerResources || context.ApplicationLabels.IgnoreQueryFilters().Any(link =>
                link.ApplicationId == application.Id && link.OwnerUserId == scope.OwnerUserId && link.LabelId == scope.LabelId)));
        if (cursor is not null)
        {
            var sort = cursor.SortValues[0];
            var id = cursor.Id;
            applications = applications.Where(application => application.AppCode.CompareTo(sort) > 0 ||
                (application.AppCode == sort && application.Id.CompareTo(id) > 0));
        }
        var applicationRows = await applications.OrderBy(application => application.AppCode).ThenBy(application => application.Id)
            .Select(application => new ApplicationRow(application.Id, application.OwnerUserId!, application.AppCode,
                application.AppName, application.OwnerTeam, application.Risk, application.Icon, application.TechStack))
            .Take(query.Limit + 1).ToListAsync(cancellationToken);
        var applicationHasNext = applicationRows.Count > query.Limit;
        if (applicationHasNext) applicationRows.RemoveAt(applicationRows.Count - 1);
        var applicationItems = applicationRows.Select(row => new ShareCatalogItemDto
        {
            Type = "APP",
            Application = new ApplicationResponseDto
            {
                Id = row.Id, OwnerUserId = row.OwnerUserId, AppCode = row.AppCode, AppName = row.AppName,
                OwnerTeam = row.OwnerTeam, Risk = row.Risk, Icon = row.Icon, TechStack = row.TechStack,
                Labels = [label], EffectivePermission = LabelEffectivePermission.Viewer,
                SharedLabelIds = [scope.LabelId], Capabilities = CatalogCapabilities.Viewer
            }
        }).ToList();
        return Page(applicationItems, applicationHasNext, endpoint, query, binding, fingerprint,
            item => [item.Application!.AppCode], item => item.Application!.Id);
    }

    public async Task<ServerResponseDto?> GetServerAsync(string userId, Guid id, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || id == Guid.Empty) return null;
        var row = await ReadableServers(userId, utcNow).Where(server => server.Id == id)
            .Select(server => new ServerRow(server.Id, server.OwnerUserId!, server.DatacenterId, server.IpAddress,
                server.Hostname, server.OsType, server.Environment, server.Datacenter != null ? server.Datacenter.Name : string.Empty, server.Status))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var view = row.OwnerUserId == userId ? CatalogView.Mine : CatalogView.Shared;
        var access = (await ServerAccessAsync(userId, view, utcNow, [row], cancellationToken))[row.Id];
        var labels = await ServerLabelsAsync([row.Id], cancellationToken);
        var applications = await ServerApplicationsAsync(userId, view, utcNow, [row.Id], cancellationToken);
        return new ServerResponseDto
        {
            Id = row.Id, OwnerUserId = row.OwnerUserId, DatacenterId = row.DatacenterId, IpAddress = row.IpAddress,
            Hostname = row.Hostname, OsType = row.OsType, Environment = row.Environment, Datacenter = row.Datacenter,
            Status = row.Status, Labels = labels.GetValueOrDefault(row.Id, []), Applications = applications.GetValueOrDefault(row.Id, []),
            EffectivePermission = access.Permission, SharedLabelIds = access.SharedLabelIds, Capabilities = access.Capabilities
        };
    }

    public async Task<ApplicationResponseDto?> GetApplicationAsync(string userId, Guid id, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || id == Guid.Empty) return null;
        var row = await ReadableApplications(userId, utcNow).Where(application => application.Id == id)
            .Select(application => new ApplicationRow(application.Id, application.OwnerUserId!, application.AppCode,
                application.AppName, application.OwnerTeam, application.Risk, application.Icon, application.TechStack))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var view = row.OwnerUserId == userId ? CatalogView.Mine : CatalogView.Shared;
        var access = (await ApplicationAccessAsync(userId, view, utcNow, [row], cancellationToken))[row.Id];
        var labels = await ApplicationLabelsAsync([row.Id], cancellationToken);
        var servers = await ApplicationServersAsync(userId, view, utcNow, [row.Id], cancellationToken);
        return new ApplicationResponseDto
        {
            Id = row.Id, OwnerUserId = row.OwnerUserId, AppCode = row.AppCode, AppName = row.AppName,
            OwnerTeam = row.OwnerTeam, Risk = row.Risk, Icon = row.Icon, TechStack = row.TechStack,
            Labels = labels.GetValueOrDefault(row.Id, []), Servers = servers.GetValueOrDefault(row.Id, []),
            EffectivePermission = access.Permission, SharedLabelIds = access.SharedLabelIds, Capabilities = access.Capabilities
        };
    }

    public async Task<int?> GetDependencyCountAsync(string userId, Guid applicationId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var readableApplications = ReadableApplications(userId, utcNow);
        var readable = await readableApplications.AnyAsync(application => application.Id == applicationId, cancellationToken);
        if (!readable) return null;
        var portIds = context.PortMappings.IgnoreQueryFilters().Where(mapping => mapping.AppId == applicationId).Select(mapping => mapping.Id);
        return await context.AppDependencies.IgnoreQueryFilters().CountAsync(dependency =>
            readableApplications.Any(application => application.Id == dependency.SourceAppId) &&
            readableApplications.Any(application => application.Id == dependency.DestAppId) &&
            (dependency.SourceAppId == applicationId || dependency.DestAppId == applicationId || portIds.Contains(dependency.DestPortId)),
            cancellationToken);
    }

    public async Task<IReadOnlyList<DeployedAppDto>?> GetDeployedApplicationsAsync(
        string userId, Guid serverId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var readableServer = await ReadableServers(userId, utcNow).AnyAsync(server => server.Id == serverId, cancellationToken);
        if (!readableServer) return null;
        var readableApplications = ReadableApplications(userId, utcNow);
        return await context.PortMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(mapping => mapping.ServerId == serverId && mapping.Application != null &&
                readableApplications.Any(application => application.Id == mapping.AppId))
            .OrderBy(mapping => mapping.Application!.AppCode).ThenBy(mapping => mapping.Id)
            .Select(mapping => new DeployedAppDto
            {
                PortMappingId = mapping.Id,
                AppId = mapping.AppId,
                AppCode = mapping.Application!.AppCode,
                AppName = mapping.Application.AppName,
                PortNumber = mapping.PortNumber
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServerResponseDto>> ExportServersAsync(
        string userId, CatalogView view, IReadOnlyCollection<Guid> ids, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var requested = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        Validate(userId, new CatalogPageQuery(view, 1));
        var rows = await AuthorizedServers(userId, view, utcNow)
            .Where(server => requested.Contains(server.Id))
            .OrderBy(server => server.Hostname).ThenBy(server => server.Id)
            .Select(server => new ServerRow(server.Id, server.OwnerUserId!, server.DatacenterId, server.IpAddress, server.Hostname,
                server.OsType, server.Environment, server.Datacenter != null ? server.Datacenter.Name : string.Empty, server.Status))
            .ToListAsync(cancellationToken);
        var access = await ServerAccessAsync(userId, view, utcNow, rows, cancellationToken);
        var labels = await ServerLabelsAsync(rows.Select(row => row.Id).ToArray(), cancellationToken);
        var applications = await ServerApplicationsAsync(userId, view, utcNow, rows.Select(row => row.Id).ToArray(), cancellationToken);
        return rows.Select(row => new ServerResponseDto
        {
            Id = row.Id, OwnerUserId = row.OwnerUserId, DatacenterId = row.DatacenterId, IpAddress = row.IpAddress,
            Hostname = row.Hostname, OsType = row.OsType, Environment = row.Environment, Datacenter = row.Datacenter,
            Status = row.Status, Labels = labels.GetValueOrDefault(row.Id, []), Applications = applications.GetValueOrDefault(row.Id, []), EffectivePermission = access[row.Id].Permission,
            SharedLabelIds = access[row.Id].SharedLabelIds, Capabilities = access[row.Id].Capabilities
        }).ToList();
    }

    public async Task<IReadOnlyList<ApplicationResponseDto>> ExportApplicationsAsync(
        string userId, CatalogView view, IReadOnlyCollection<Guid> ids, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var requested = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        Validate(userId, new CatalogPageQuery(view, 1));
        var rows = await AuthorizedApplications(userId, view, utcNow)
            .Where(application => requested.Contains(application.Id))
            .OrderBy(application => application.AppCode).ThenBy(application => application.Id)
            .Select(application => new ApplicationRow(application.Id, application.OwnerUserId!, application.AppCode,
                application.AppName, application.OwnerTeam, application.Risk, application.Icon, application.TechStack))
            .ToListAsync(cancellationToken);
        var access = await ApplicationAccessAsync(userId, view, utcNow, rows, cancellationToken);
        var labels = await ApplicationLabelsAsync(rows.Select(row => row.Id).ToArray(), cancellationToken);
        var servers = await ApplicationServersAsync(userId, view, utcNow, rows.Select(row => row.Id).ToArray(), cancellationToken);
        return rows.Select(row => new ApplicationResponseDto
        {
            Id = row.Id, OwnerUserId = row.OwnerUserId, AppCode = row.AppCode, AppName = row.AppName,
            OwnerTeam = row.OwnerTeam, Risk = row.Risk, Icon = row.Icon, TechStack = row.TechStack,
            Labels = labels.GetValueOrDefault(row.Id, []), Servers = servers.GetValueOrDefault(row.Id, []), EffectivePermission = access[row.Id].Permission,
            SharedLabelIds = access[row.Id].SharedLabelIds, Capabilities = access[row.Id].Capabilities
        }).ToList();
    }

    public async Task<CursorPageDto<ServerResponseDto>> GetServersAsync(
        string userId,
        CatalogPageQuery query,
        DateTime utcNow,
        string? ownerUserId = null,
        string? labelKey = null,
        string? labelValue = null,
        CancellationToken cancellationToken = default)
    {
        Validate(userId, query);
        var normalizedOwnerUserId = ownerUserId?.Trim();
        var normalizedLabelKey = labelKey?.Trim();
        var normalizedLabelValue = labelValue?.Trim();
        var fingerprint = CatalogFilterFingerprint.Resources(normalizedOwnerUserId, normalizedLabelKey, normalizedLabelValue);
        var cursor = Position("servers", query, userId, fingerprint, 1);
        var authorized = AuthorizedServers(userId, query.View, utcNow);
        if (!string.IsNullOrWhiteSpace(normalizedOwnerUserId))
            authorized = authorized.Where(server => server.OwnerUserId == normalizedOwnerUserId);
        if (!string.IsNullOrWhiteSpace(normalizedLabelKey))
        {
            authorized = authorized.Where(server =>
                context.ServerLabels.IgnoreQueryFilters().Any(link =>
                    link.ServerId == server.Id &&
                    link.OwnerUserId == server.OwnerUserId &&
                    link.Label != null && link.Label.Key == normalizedLabelKey &&
                    (string.IsNullOrWhiteSpace(normalizedLabelValue) || link.Label.Value == normalizedLabelValue)));
        }
        if (cursor is not null)
        {
            var hostname = cursor.SortValues[0];
            var id = cursor.Id;
            authorized = authorized.Where(server =>
                server.Hostname.CompareTo(hostname) > 0 ||
                (server.Hostname == hostname && server.Id.CompareTo(id) > 0));
        }

        var rows = await authorized
            .OrderBy(server => server.Hostname).ThenBy(server => server.Id)
            .Select(server => new ServerRow(
                server.Id,
                server.OwnerUserId!,
                server.DatacenterId,
                server.IpAddress,
                server.Hostname,
                server.OsType,
                server.Environment,
                server.Datacenter != null ? server.Datacenter.Name : string.Empty,
                server.Status))
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        var hasNext = rows.Count > query.Limit;
        if (hasNext) rows.RemoveAt(rows.Count - 1);

        var access = await ServerAccessAsync(userId, query.View, utcNow, rows, cancellationToken);
        var labels = await ServerLabelsAsync(rows.Select(row => row.Id).ToArray(), cancellationToken);
        var applications = await ServerApplicationsAsync(userId, query.View, utcNow, rows.Select(row => row.Id).ToArray(), cancellationToken);
        var items = rows.Select(row =>
        {
            var permission = access[row.Id];
            return new ServerResponseDto
            {
                Id = row.Id,
                OwnerUserId = row.OwnerUserId,
                DatacenterId = row.DatacenterId,
                IpAddress = row.IpAddress,
                Hostname = row.Hostname,
                OsType = row.OsType,
                Environment = row.Environment,
                Datacenter = row.Datacenter,
                Status = row.Status,
                Labels = labels.GetValueOrDefault(row.Id, []),
                Applications = applications.GetValueOrDefault(row.Id, []),
                EffectivePermission = permission.Permission,
                SharedLabelIds = permission.SharedLabelIds,
                Capabilities = permission.Capabilities
            };
        }).ToList();

        return Page(items, hasNext, "servers", query, userId, fingerprint, item => [item.Hostname], item => item.Id);
    }

    public async Task<CursorPageDto<ApplicationResponseDto>> GetApplicationsAsync(
        string userId,
        CatalogPageQuery query,
        DateTime utcNow,
        string? labelKey = null,
        string? labelValue = null,
        string? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        Validate(userId, query);
        var normalizedLabelKey = labelKey?.Trim();
        var normalizedLabelValue = labelValue?.Trim();
        var normalizedOwnerUserId = ownerUserId?.Trim();
        var fingerprint = CatalogFilterFingerprint.Resources(normalizedOwnerUserId, normalizedLabelKey, normalizedLabelValue);
        var cursor = Position("applications", query, userId, fingerprint, 1);
        var authorized = AuthorizedApplications(userId, query.View, utcNow);
        if (!string.IsNullOrWhiteSpace(normalizedOwnerUserId))
            authorized = authorized.Where(application => application.OwnerUserId == normalizedOwnerUserId);
        if (!string.IsNullOrWhiteSpace(normalizedLabelKey))
        {
            authorized = authorized.Where(application =>
                context.ApplicationLabels.IgnoreQueryFilters().Any(link =>
                    link.ApplicationId == application.Id &&
                    link.OwnerUserId == application.OwnerUserId &&
                    link.Label != null && link.Label.Key == normalizedLabelKey &&
                    (string.IsNullOrWhiteSpace(normalizedLabelValue) || link.Label.Value == normalizedLabelValue)));
        }
        if (cursor is not null)
        {
            var appCode = cursor.SortValues[0];
            var id = cursor.Id;
            authorized = authorized.Where(application =>
                application.AppCode.CompareTo(appCode) > 0 ||
                (application.AppCode == appCode && application.Id.CompareTo(id) > 0));
        }

        var rows = await authorized
            .OrderBy(application => application.AppCode).ThenBy(application => application.Id)
            .Select(application => new ApplicationRow(
                application.Id,
                application.OwnerUserId!,
                application.AppCode,
                application.AppName,
                application.OwnerTeam,
                application.Risk,
                application.Icon,
                application.TechStack))
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        var hasNext = rows.Count > query.Limit;
        if (hasNext) rows.RemoveAt(rows.Count - 1);

        var access = await ApplicationAccessAsync(userId, query.View, utcNow, rows, cancellationToken);
        var labels = await ApplicationLabelsAsync(rows.Select(row => row.Id).ToArray(), cancellationToken);
        var servers = await ApplicationServersAsync(userId, query.View, utcNow, rows.Select(row => row.Id).ToArray(), cancellationToken);
        var items = rows.Select(row =>
        {
            var permission = access[row.Id];
            return new ApplicationResponseDto
            {
                Id = row.Id,
                OwnerUserId = row.OwnerUserId,
                AppCode = row.AppCode,
                AppName = row.AppName,
                OwnerTeam = row.OwnerTeam,
                Risk = row.Risk,
                Icon = row.Icon,
                TechStack = row.TechStack,
                Labels = labels.GetValueOrDefault(row.Id, []),
                Servers = servers.GetValueOrDefault(row.Id, []),
                EffectivePermission = permission.Permission,
                SharedLabelIds = permission.SharedLabelIds,
                Capabilities = permission.Capabilities
            };
        }).ToList();

        return Page(items, hasNext, "applications", query, userId, fingerprint, item => [item.AppCode], item => item.Id);
    }

    public async Task<CursorPageDto<DatacenterDto>> GetDatacentersAsync(
        string userId,
        CatalogPageQuery query,
        DateTime utcNow,
        string? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        Validate(userId, query);
        var normalizedOwnerUserId = ownerUserId?.Trim();
        var fingerprint = CatalogFilterFingerprint.Resources(normalizedOwnerUserId);
        var cursor = Position("datacenters", query, userId, fingerprint, 1);
        var datacenters = context.Datacenters.IgnoreQueryFilters().AsNoTracking()
            .Where(datacenter => datacenter.OwnerUserId != null);
        datacenters = query.View == CatalogView.Mine
            ? datacenters.Where(datacenter => datacenter.OwnerUserId == userId)
            : datacenters.Where(datacenter => datacenter.OwnerUserId != userId &&
                AuthorizedServers(userId, CatalogView.Shared, utcNow)
                    .Any(server => server.DatacenterId == datacenter.Id && server.OwnerUserId == datacenter.OwnerUserId));
        if (!string.IsNullOrWhiteSpace(normalizedOwnerUserId))
            datacenters = datacenters.Where(datacenter => datacenter.OwnerUserId == normalizedOwnerUserId);
        if (cursor is not null)
        {
            var name = cursor.SortValues[0];
            var id = cursor.Id;
            datacenters = datacenters.Where(datacenter =>
                datacenter.Name.CompareTo(name) > 0 ||
                (datacenter.Name == name && datacenter.Id.CompareTo(id) > 0));
        }

        var rows = await datacenters.OrderBy(datacenter => datacenter.Name).ThenBy(datacenter => datacenter.Id)
            .Select(datacenter => new DatacenterRow(datacenter.Id, datacenter.OwnerUserId!, datacenter.Name, datacenter.Location))
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        var hasNext = rows.Count > query.Limit;
        if (hasNext) rows.RemoveAt(rows.Count - 1);

        Dictionary<Guid, AccessRow> access;
        if (query.View == CatalogView.Mine)
        {
            access = rows.ToDictionary(row => row.Id, _ => OwnerAccess());
        }
        else
        {
            var ids = rows.Select(row => row.Id).ToArray();
            var servers = await AuthorizedServers(userId, CatalogView.Shared, utcNow)
                .Where(server => ids.Contains(server.DatacenterId))
                .Select(server => new ServerRow(server.Id, server.OwnerUserId!, server.DatacenterId, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty))
                .ToListAsync(cancellationToken);
            var serverAccess = await ServerAccessAsync(userId, CatalogView.Shared, utcNow, servers, cancellationToken);
            access = rows.ToDictionary(
                row => row.Id,
                row => AggregateReadOnly(servers.Where(server => server.DatacenterId == row.Id).Select(server => serverAccess[server.Id])));
        }

        var items = rows.Select(row => new DatacenterDto
        {
            Id = row.Id,
            OwnerUserId = row.OwnerUserId,
            Name = row.Name,
            Location = row.Location,
            EffectivePermission = query.View == CatalogView.Mine
                ? LabelEffectivePermission.Owner
                : LabelEffectivePermission.Viewer,
            SharedLabelIds = access[row.Id].SharedLabelIds,
            Capabilities = query.View == CatalogView.Mine ? CatalogCapabilities.Owner : CatalogCapabilities.ReadOnly
        }).ToList();
        return Page(items, hasNext, "datacenters", query, userId, fingerprint, item => [item.Name], item => item.Id);
    }

    public async Task<CursorPageDto<CatalogLabelDto>> GetLabelsAsync(
        string userId,
        CatalogPageQuery query,
        DateTime utcNow,
        string? ownerUserId = null,
        string? labelKey = null,
        string? labelValue = null,
        CancellationToken cancellationToken = default)
    {
        Validate(userId, query);
        var normalizedOwnerUserId = ownerUserId?.Trim();
        var normalizedLabelKey = labelKey?.Trim();
        var normalizedLabelValue = labelValue?.Trim();
        var fingerprint = CatalogFilterFingerprint.Resources(normalizedOwnerUserId, normalizedLabelKey, normalizedLabelValue);
        var cursor = Position("labels", query, userId, fingerprint, 2);
        var activeGrants = ActiveGrants(userId, utcNow);
        var labels = context.Labels.IgnoreQueryFilters().AsNoTracking()
            .Where(label => label.OwnerUserId != null);
        labels = query.View == CatalogView.Mine
            ? labels.Where(label => label.OwnerUserId == userId)
            : labels.Where(label => label.OwnerUserId != userId && activeGrants.Any(grant => grant.LabelId == label.Id && grant.OwnerUserId == label.OwnerUserId));
        if (!string.IsNullOrWhiteSpace(normalizedOwnerUserId))
            labels = labels.Where(label => label.OwnerUserId == normalizedOwnerUserId);
        if (!string.IsNullOrWhiteSpace(normalizedLabelKey))
            labels = labels.Where(label => label.Key == normalizedLabelKey &&
                (string.IsNullOrWhiteSpace(normalizedLabelValue) || label.Value == normalizedLabelValue));
        if (cursor is not null)
        {
            var key = cursor.SortValues[0];
            var value = cursor.SortValues[1];
            var id = cursor.Id;
            labels = labels.Where(label =>
                label.Key.CompareTo(key) > 0 ||
                (label.Key == key && label.Value.CompareTo(value) > 0) ||
                (label.Key == key && label.Value == value && label.Id.CompareTo(id) > 0));
        }

        var rows = await labels.OrderBy(label => label.Key).ThenBy(label => label.Value).ThenBy(label => label.Id)
            .Select(label => new LabelRow(label.Id, label.OwnerUserId!, label.Key, label.Value, label.Kind, label.IsProtected))
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        var hasNext = rows.Count > query.Limit;
        if (hasNext) rows.RemoveAt(rows.Count - 1);

        var permissionByLabel = new Dictionary<Guid, AccessRow>();
        if (query.View == CatalogView.Mine)
        {
            foreach (var row in rows) permissionByLabel[row.Id] = OwnerAccess();
        }
        else
        {
            var ids = rows.Select(row => row.Id).ToArray();
            var grants = await activeGrants.Where(grant => ids.Contains(grant.LabelId))
                .Select(grant => new GrantRow(grant.LabelId, grant.OwnerUserId, grant.Permission, grant.Label!.Kind))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                var matching = grants.Where(grant => grant.LabelId == row.Id).ToList();
                permissionByLabel[row.Id] = SharedAccess(matching, [row.Id]);
            }
        }

        var items = rows.Select(row => new CatalogLabelDto
        {
            Id = row.Id,
            OwnerUserId = row.OwnerUserId,
            Key = row.Key,
            Value = row.Value,
            Kind = row.Kind,
            IsProtected = row.IsProtected,
            EffectivePermission = permissionByLabel[row.Id].Permission,
            SharedLabelIds = permissionByLabel[row.Id].SharedLabelIds,
            Capabilities = permissionByLabel[row.Id].Capabilities
        }).ToList();
        return Page(items, hasNext, "labels", query, userId, fingerprint, item => [item.Key, item.Value], item => item.Id);
    }

    public async Task<CursorPageDto<SearchResultDto>> SearchAsync(
        string userId,
        string keyword,
        CatalogPageQuery query,
        DateTime utcNow,
        string? ownerUserId = null,
        string? labelKey = null,
        string? labelValue = null,
        CancellationToken cancellationToken = default)
    {
        Validate(userId, query);
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Trim().Length < 2)
            return new CursorPageDto<SearchResultDto>([], null, false);
        var search = keyword.Trim().ToLower();
        var normalizedOwnerUserId = ownerUserId?.Trim();
        var normalizedLabelKey = labelKey?.Trim();
        var normalizedLabelValue = labelValue?.Trim();
        var fingerprint = CatalogFilterFingerprint.Search(search, normalizedOwnerUserId, normalizedLabelKey, normalizedLabelValue);
        var cursor = Position("search", query, userId, fingerprint, 2);
        var serverEntities = AuthorizedServers(userId, query.View, utcNow);
        var applicationEntities = AuthorizedApplications(userId, query.View, utcNow);
        if (!string.IsNullOrWhiteSpace(normalizedOwnerUserId))
        {
            serverEntities = serverEntities.Where(server => server.OwnerUserId == normalizedOwnerUserId);
            applicationEntities = applicationEntities.Where(application => application.OwnerUserId == normalizedOwnerUserId);
        }
        if (!string.IsNullOrWhiteSpace(normalizedLabelKey))
        {
            serverEntities = serverEntities.Where(server => context.ServerLabels.IgnoreQueryFilters().Any(link =>
                link.ServerId == server.Id && link.OwnerUserId == server.OwnerUserId && link.Label != null &&
                link.Label.Key == normalizedLabelKey &&
                (string.IsNullOrWhiteSpace(normalizedLabelValue) || link.Label.Value == normalizedLabelValue)));
            applicationEntities = applicationEntities.Where(application => context.ApplicationLabels.IgnoreQueryFilters().Any(link =>
                link.ApplicationId == application.Id && link.OwnerUserId == application.OwnerUserId && link.Label != null &&
                link.Label.Key == normalizedLabelKey &&
                (string.IsNullOrWhiteSpace(normalizedLabelValue) || link.Label.Value == normalizedLabelValue)));
        }
        var servers = serverEntities
            .Where(server => server.Hostname.ToLower().Contains(search) || server.IpAddress.ToLower().Contains(search))
            .Select(server => new SearchRow(
                server.Id, server.OwnerUserId!, "SERVER", server.Hostname, server.IpAddress,
                server.Hostname.ToLower().Contains(search) ? "Matched by Server Hostname" : "Matched by Server IP"));
        var applications = applicationEntities
            .Where(application => application.AppName.ToLower().Contains(search) || application.AppCode.ToLower().Contains(search))
            .Select(application => new SearchRow(
                application.Id, application.OwnerUserId!, "APP", application.AppName, application.AppCode,
                application.AppName.ToLower().Contains(search) ? "Matched by App Name" : "Matched by App Code"));
        if (cursor is not null)
        {
            var type = cursor.SortValues[0];
            var title = cursor.SortValues[1];
            var id = cursor.Id;
            if (type == "APP")
            {
                applications = applications.Where(result =>
                    result.Title.CompareTo(title) > 0 ||
                    (result.Title == title && result.Id.CompareTo(id) > 0));
            }
            else if (type == "SERVER")
            {
                applications = applications.Where(_ => false);
                servers = servers.Where(result =>
                    result.Title.CompareTo(title) > 0 ||
                    (result.Title == title && result.Id.CompareTo(id) > 0));
            }
            else
            {
                throw new CatalogQueryValidationException("The catalog cursor has an invalid search resource type.");
            }
        }

        // APP sorts before SERVER. Each source's title/id ordering is performed entirely
        // by PostgreSQL, so pagination never compares database collation with .NET collation.
        var serverRows = await servers.OrderBy(result => result.Title).ThenBy(result => result.Id)
            .Take(query.Limit + 1).ToListAsync(cancellationToken);
        var applicationRows = await applications.OrderBy(result => result.Title).ThenBy(result => result.Id)
            .Take(query.Limit + 1).ToListAsync(cancellationToken);
        var rows = applicationRows.Concat(serverRows)
            .Take(query.Limit + 1)
            .ToList();
        var hasNext = rows.Count > query.Limit;
        if (hasNext) rows.RemoveAt(rows.Count - 1);

        var serverResources = rows.Where(row => row.Type == "SERVER")
            .Select(row => new ServerRow(row.Id, row.OwnerUserId, Guid.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty)).ToList();
        var applicationResources = rows.Where(row => row.Type == "APP")
            .Select(row => new ApplicationRow(row.Id, row.OwnerUserId, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty)).ToList();
        var serverAccess = await ServerAccessAsync(userId, query.View, utcNow, serverResources, cancellationToken);
        var applicationAccess = await ApplicationAccessAsync(userId, query.View, utcNow, applicationResources, cancellationToken);
        var items = rows.Select(row =>
        {
            var access = row.Type == "SERVER" ? serverAccess[row.Id] : applicationAccess[row.Id];
            return new SearchResultDto
            {
                Id = row.Id,
                OwnerUserId = row.OwnerUserId,
                Type = row.Type,
                Title = row.Title,
                Subtitle = row.Subtitle,
                MatchReason = row.MatchReason,
                EffectivePermission = access.Permission,
                SharedLabelIds = access.SharedLabelIds,
                Capabilities = access.Capabilities
            };
        }).ToList();
        return Page(items, hasNext, "search", query, userId, fingerprint, item => [item.Type, item.Title], item => item.Id);
    }

    private IQueryable<Server> AuthorizedServers(string userId, CatalogView view, DateTime utcNow)
    {
        var servers = context.Servers.IgnoreQueryFilters().AsNoTracking()
            .Where(server => server.OwnerUserId != null);
        if (view == CatalogView.Mine) return servers.Where(server => server.OwnerUserId == userId);
        var grants = ActiveGrants(userId, utcNow);
        return servers.Where(server => server.OwnerUserId != userId && grants.Any(grant =>
            grant.OwnerUserId == server.OwnerUserId && grant.Label != null &&
            (grant.Label.Kind == LabelKinds.Owner ||
             (grant.Label.Kind == LabelKinds.Business && context.ServerLabels.IgnoreQueryFilters().Any(link =>
                 link.ServerId == server.Id && link.OwnerUserId == server.OwnerUserId && link.LabelId == grant.LabelId)))));
    }

    private IQueryable<Server> ReadableServers(string userId, DateTime utcNow)
    {
        var grants = ActiveGrants(userId, utcNow);
        return context.Servers.IgnoreQueryFilters().AsNoTracking().Where(server => server.OwnerUserId != null &&
            (server.OwnerUserId == userId || (server.OwnerUserId != userId && grants.Any(grant =>
                grant.OwnerUserId == server.OwnerUserId && grant.Label != null &&
                (grant.Label.Kind == LabelKinds.Owner ||
                 (grant.Label.Kind == LabelKinds.Business && context.ServerLabels.IgnoreQueryFilters().Any(link =>
                     link.ServerId == server.Id && link.OwnerUserId == server.OwnerUserId && link.LabelId == grant.LabelId)))))));
    }

    private IQueryable<AppEntity> AuthorizedApplications(string userId, CatalogView view, DateTime utcNow)
    {
        var applications = context.Applications.IgnoreQueryFilters().AsNoTracking()
            .Where(application => application.OwnerUserId != null);
        if (view == CatalogView.Mine) return applications.Where(application => application.OwnerUserId == userId);
        var grants = ActiveGrants(userId, utcNow);
        return applications.Where(application => application.OwnerUserId != userId && grants.Any(grant =>
            grant.OwnerUserId == application.OwnerUserId && grant.Label != null &&
            (grant.Label.Kind == LabelKinds.Owner ||
             (grant.Label.Kind == LabelKinds.Business && context.ApplicationLabels.IgnoreQueryFilters().Any(link =>
                 link.ApplicationId == application.Id && link.OwnerUserId == application.OwnerUserId && link.LabelId == grant.LabelId)))));
    }

    private IQueryable<AppEntity> ReadableApplications(string userId, DateTime utcNow)
    {
        var grants = ActiveGrants(userId, utcNow);
        return context.Applications.IgnoreQueryFilters().AsNoTracking().Where(application => application.OwnerUserId != null &&
            (application.OwnerUserId == userId || (application.OwnerUserId != userId && grants.Any(grant =>
                grant.OwnerUserId == application.OwnerUserId && grant.Label != null &&
                (grant.Label.Kind == LabelKinds.Owner ||
                 (grant.Label.Kind == LabelKinds.Business && context.ApplicationLabels.IgnoreQueryFilters().Any(link =>
                     link.ApplicationId == application.Id && link.OwnerUserId == application.OwnerUserId && link.LabelId == grant.LabelId)))))));
    }

    private IQueryable<LabelGrant> ActiveGrants(string userId, DateTime utcNow) =>
        context.LabelGrants.IgnoreQueryFilters().AsNoTracking().Where(grant =>
            grant.GranteeUserId == userId && grant.TokenHash == null && grant.RevokedAt == null &&
            (grant.ExpiresAt == null || grant.ExpiresAt > utcNow) &&
            grant.OwnerUserId != string.Empty && grant.Label != null && grant.Label.OwnerUserId != null &&
            grant.Label.OwnerUserId == grant.OwnerUserId);

    private async Task<Dictionary<Guid, AccessRow>> ServerAccessAsync(
        string userId, CatalogView view, DateTime utcNow, IReadOnlyCollection<ServerRow> resources, CancellationToken cancellationToken)
    {
        if (view == CatalogView.Mine) return resources.ToDictionary(resource => resource.Id, _ => OwnerAccess());
        var ids = resources.Select(resource => resource.Id).ToArray();
        var links = await context.ServerLabels.IgnoreQueryFilters().AsNoTracking()
            .Where(link => ids.Contains(link.ServerId))
            .Select(link => new ResourceLabelRow(link.ServerId, link.LabelId))
            .ToListAsync(cancellationToken);
        return await SharedResourceAccessAsync(userId, utcNow, resources.Select(resource => new ResourceRow(resource.Id, resource.OwnerUserId)).ToList(), links, cancellationToken);
    }

    private async Task<Dictionary<Guid, AccessRow>> ApplicationAccessAsync(
        string userId, CatalogView view, DateTime utcNow, IReadOnlyCollection<ApplicationRow> resources, CancellationToken cancellationToken)
    {
        if (view == CatalogView.Mine) return resources.ToDictionary(resource => resource.Id, _ => OwnerAccess());
        var ids = resources.Select(resource => resource.Id).ToArray();
        var links = await context.ApplicationLabels.IgnoreQueryFilters().AsNoTracking()
            .Where(link => ids.Contains(link.ApplicationId))
            .Select(link => new ResourceLabelRow(link.ApplicationId, link.LabelId))
            .ToListAsync(cancellationToken);
        return await SharedResourceAccessAsync(userId, utcNow, resources.Select(resource => new ResourceRow(resource.Id, resource.OwnerUserId)).ToList(), links, cancellationToken);
    }

    private async Task<Dictionary<Guid, AccessRow>> SharedResourceAccessAsync(
        string userId,
        DateTime utcNow,
        IReadOnlyCollection<ResourceRow> resources,
        IReadOnlyCollection<ResourceLabelRow> links,
        CancellationToken cancellationToken)
    {
        if (resources.Count == 0) return [];
        var owners = resources.Select(resource => resource.OwnerUserId).Distinct().ToArray();
        var grants = await ActiveGrants(userId, utcNow).Where(grant => owners.Contains(grant.OwnerUserId))
            .Select(grant => new GrantRow(grant.LabelId, grant.OwnerUserId, grant.Permission, grant.Label!.Kind))
            .ToListAsync(cancellationToken);
        return resources.ToDictionary(resource => resource.Id, resource =>
        {
            var businessLabels = links.Where(link => link.ResourceId == resource.Id).Select(link => link.LabelId).ToHashSet();
            var matching = grants.Where(grant => grant.OwnerUserId == resource.OwnerUserId &&
                (grant.LabelKind == LabelKinds.Owner || businessLabels.Contains(grant.LabelId))).ToList();
            return SharedAccess(matching, matching.Select(grant => grant.LabelId));
        });
    }

    private async Task<Dictionary<Guid, List<LabelDto>>> ServerLabelsAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0) return [];
        var rows = await context.ServerLabels.IgnoreQueryFilters().AsNoTracking()
            .Where(link => ids.Contains(link.ServerId) && link.Label != null)
            .Select(link => new LabelValueRow(link.ServerId, link.Label!.Key, link.Label.Value))
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.ResourceId).ToDictionary(
            group => group.Key,
            group => group.Select(row => new LabelDto { Key = row.Key, Value = row.Value }).ToList());
    }

    private async Task<Dictionary<Guid, List<LabelDto>>> ApplicationLabelsAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0) return [];
        var rows = await context.ApplicationLabels.IgnoreQueryFilters().AsNoTracking()
            .Where(link => ids.Contains(link.ApplicationId) && link.Label != null)
            .Select(link => new LabelValueRow(link.ApplicationId, link.Label!.Key, link.Label.Value))
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.ResourceId).ToDictionary(
            group => group.Key,
            group => group.Select(row => new LabelDto { Key = row.Key, Value = row.Value }).ToList());
    }

    private async Task<Dictionary<Guid, List<ApplicationOnServerDto>>> ServerApplicationsAsync(
        string userId, CatalogView view, DateTime utcNow, Guid[] serverIds, CancellationToken cancellationToken)
    {
        if (serverIds.Length == 0) return [];
        var authorizedApplications = AuthorizedApplications(userId, view, utcNow);
        var rows = await context.PortMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(mapping => serverIds.Contains(mapping.ServerId) && mapping.Application != null &&
                authorizedApplications.Any(application => application.Id == mapping.AppId && application.OwnerUserId == mapping.OwnerUserId))
            .Select(mapping => new ServerApplicationRow(
                mapping.ServerId, mapping.Id, mapping.AppId, mapping.Application!.AppCode, mapping.Application.AppName,
                mapping.Application.OwnerTeam, mapping.PortNumber, mapping.Protocol))
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.ServerId).ToDictionary(group => group.Key, group => group.Select(row => new ApplicationOnServerDto
        {
            PortMappingId = row.PortMappingId, Id = row.ApplicationId, AppCode = row.AppCode, AppName = row.AppName,
            OwnerTeam = row.OwnerTeam, PortNumber = row.PortNumber, Protocol = row.Protocol
        }).ToList());
    }

    private async Task<Dictionary<Guid, List<ServerOnApplicationDto>>> ApplicationServersAsync(
        string userId, CatalogView view, DateTime utcNow, Guid[] applicationIds, CancellationToken cancellationToken)
    {
        if (applicationIds.Length == 0) return [];
        var authorizedServers = AuthorizedServers(userId, view, utcNow);
        var rows = await context.PortMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(mapping => applicationIds.Contains(mapping.AppId) && mapping.Server != null &&
                authorizedServers.Any(server => server.Id == mapping.ServerId && server.OwnerUserId == mapping.OwnerUserId))
            .Select(mapping => new ApplicationServerRow(
                mapping.AppId, mapping.Id, mapping.ServerId, mapping.Server!.Hostname, mapping.Server.IpAddress,
                mapping.PortNumber, mapping.Protocol))
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.ApplicationId).ToDictionary(group => group.Key, group => group.Select(row => new ServerOnApplicationDto
        {
            PortMappingId = row.PortMappingId, Id = row.ServerId, Hostname = row.Hostname, IpAddress = row.IpAddress,
            PortNumber = row.PortNumber, Protocol = row.Protocol
        }).ToList());
    }

    private CatalogCursorPosition? Position(string endpoint, CatalogPageQuery query, string principalBinding, string filterFingerprint, int sortValues) =>
        string.IsNullOrWhiteSpace(query.Cursor) ? null : cursors.Decode(endpoint, query.View, principalBinding, filterFingerprint, query.Cursor, sortValues);

    private static void Validate(string userId, CatalogPageQuery query)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new UnauthorizedAccessException();
        if (query.View is not CatalogView.Mine and not CatalogView.Shared)
            throw new CatalogQueryValidationException("Catalog view must be 'mine' or 'shared'.");
        if (query.Limit is < 1 or > 100)
            throw new CatalogQueryValidationException("Catalog limit must be between 1 and 100.");
    }

    private CursorPageDto<T> Page<T>(
        IReadOnlyList<T> items,
        bool hasNext,
        string endpoint,
        CatalogPageQuery query,
        string principalBinding,
        string filterFingerprint,
        Func<T, IReadOnlyList<string>> sort,
        Func<T, Guid> id)
    {
        var next = hasNext && items.Count > 0
            ? cursors.Encode(endpoint, query.View, principalBinding, filterFingerprint, sort(items[^1]), id(items[^1]))
            : null;
        return new CursorPageDto<T>(items, next, hasNext);
    }

    private static AccessRow OwnerAccess() => new(LabelEffectivePermission.Owner, [], CatalogCapabilities.Owner);

    private static AccessRow SharedAccess(IReadOnlyCollection<GrantRow> grants, IEnumerable<Guid> sharedLabelIds)
    {
        var permission = grants.Any(grant => grant.Permission == LabelGrantPermissions.Editor)
            ? LabelEffectivePermission.Editor
            : LabelEffectivePermission.Viewer;
        return new AccessRow(
            permission,
            sharedLabelIds.Distinct().Order().ToArray(),
            permission == LabelEffectivePermission.Editor ? CatalogCapabilities.Editor : CatalogCapabilities.Viewer);
    }

    private static AccessRow AggregateReadOnly(IEnumerable<AccessRow> values)
    {
        var rows = values.ToList();
        return new AccessRow(
            LabelEffectivePermission.Viewer,
            rows.SelectMany(row => row.SharedLabelIds).Distinct().Order().ToArray(),
            CatalogCapabilities.ReadOnly);
    }

    private sealed record ServerRow(Guid Id, string OwnerUserId, Guid DatacenterId, string IpAddress, string Hostname, string OsType, string Environment, string Datacenter, string Status);
    private sealed record ApplicationRow(Guid Id, string OwnerUserId, string AppCode, string AppName, string OwnerTeam, string Risk, string Icon, string TechStack);
    private sealed record DatacenterRow(Guid Id, string OwnerUserId, string Name, string Location);
    private sealed record LabelRow(Guid Id, string OwnerUserId, string Key, string Value, string Kind, bool IsProtected);
    private sealed record SearchRow(Guid Id, string OwnerUserId, string Type, string Title, string Subtitle, string MatchReason);
    private sealed record ResourceRow(Guid Id, string OwnerUserId);
    private sealed record ResourceLabelRow(Guid ResourceId, Guid LabelId);
    private sealed record LabelValueRow(Guid ResourceId, string Key, string Value);
    private sealed record GrantRow(Guid LabelId, string OwnerUserId, string Permission, string LabelKind);
    private sealed record AccessRow(LabelEffectivePermission Permission, IReadOnlyList<Guid> SharedLabelIds, LabelAccessCapabilities Capabilities);
    private sealed record ServerApplicationRow(Guid ServerId, Guid PortMappingId, Guid ApplicationId, string AppCode, string AppName, string OwnerTeam, int PortNumber, string Protocol);
    private sealed record ApplicationServerRow(Guid ApplicationId, Guid PortMappingId, Guid ServerId, string Hostname, string IpAddress, int PortNumber, string Protocol);
}

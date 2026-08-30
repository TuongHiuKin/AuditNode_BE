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
    private readonly IScopedResourcePolicy _scopePolicy;
    private readonly ICurrentUserService _currentUser;
    private readonly IGlobalCatalogRepository _catalog;
    private readonly TimeProvider _timeProvider;

    public ServerService(IServerRepository repository, ITenantProvider tenantProvider, IScopedResourcePolicy scopePolicy, ICurrentUserService currentUser, IGlobalCatalogRepository catalog, TimeProvider timeProvider)
    {
        _repository = repository;
        _tenantProvider = tenantProvider;
        _scopePolicy = scopePolicy;
        _currentUser = currentUser;
        _catalog = catalog;
        _timeProvider = timeProvider;
    }

    public Task<CursorPageDto<ServerResponseDto>> GetCatalogPageAsync(CatalogPageQuery query, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult(new CursorPageDto<ServerResponseDto>([], null, false))
            : _catalog.GetServersAsync(_currentUser.UserId!, query, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public Task<ServerResponseDto?> GetCatalogDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult<ServerResponseDto?>(null)
            : _catalog.GetServerAsync(_currentUser.UserId!, id, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public Task<IReadOnlyList<ServerResponseDto>> ExportCatalogAsync(IReadOnlyCollection<Guid> ids, CatalogView view, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult<IReadOnlyList<ServerResponseDto>>([])
            : _catalog.ExportServersAsync(_currentUser.UserId!, view, ids, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<IEnumerable<ServerResponseDto>> GetServersAsync()
    {
        if (!HasWorkspace())
            return Array.Empty<ServerResponseDto>();

        var serverIds = await ReadableIdsAsync("server");
        var appIds = await ReadableIdsAsync("application");
        return await _repository.GetScopedAsync(serverIds, appIds);
    }

    public async Task<ServerResponseDto?> GetServerAsync(Guid id)
    {
        if (!HasWorkspace() || id == Guid.Empty)
            return null;

        var server = await _repository.GetByIdAsync(id);
        if (server is not null && !await CanAsync("server", id, false)) return null;
        return server is null ? null : Map(server);
    }

    public async Task<ServerOperationResult> CreateServerAsync(CreateServerDto dto)
    {
        if (!HasWorkspace())
            return new(ServerOperationStatus.InvalidWorkspace);
        if (!await CanCreateAsync(dto.Labels)) return new(ServerOperationStatus.Forbidden);

        if (!await _repository.DatacenterExistsAsync(dto.DatacenterId))
            return new(ServerOperationStatus.DatacenterNotFound);

        var normalizedIp = NormalizeIp(dto.IpAddress);
        if (await _repository.IpAddressExistsAsync(normalizedIp, null))
            return new(ServerOperationStatus.DuplicateIp);

        var server = new Server
        {
            Id = Guid.NewGuid(),
            OwnerUserId = _currentUser.UserId,
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
        if (!await CanAsync("server", id, true)) return new(ServerOperationStatus.Forbidden);
        if (dto.Labels is not null && !await CanCreateAsync(dto.Labels)) return new(ServerOperationStatus.Forbidden);

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
        if (!await CanAsync("server", id, true)) return ServerOperationStatus.Forbidden;

        await _repository.DeleteAsync(server);
        return ServerOperationStatus.Success;
    }

    public async Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids)
    {
        if (!HasWorkspace())
            return Array.Empty<ServerResponseDto>();

        return await _repository.GetScopedAsync(await ReadableIdsAsync("server"), await ReadableIdsAsync("application"), ids.Where(id => id != Guid.Empty).Distinct());
    }

    private async Task<IEnumerable<ServerResponseDto>> FilterReadableAsync(IReadOnlyCollection<ServerResponseDto> servers)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId) || !_tenantProvider.WorkspaceId.HasValue) return [];
        var allowed = new List<ServerResponseDto>();
        foreach (var server in servers)
            if (await _scopePolicy.CanReadAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, "server", server.Id)) allowed.Add(server);
        return allowed;
    }

    private Task<IReadOnlySet<Guid>?> ReadableIdsAsync(string type) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId) || !_tenantProvider.WorkspaceId.HasValue
            ? Task.FromResult<IReadOnlySet<Guid>?>(new HashSet<Guid>())
            : _scopePolicy.GetReadableIdsAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, type);

    private Task<bool> CanAsync(string type, Guid id, bool write) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId) || !_tenantProvider.WorkspaceId.HasValue
            ? Task.FromResult(false)
            : write
                ? _scopePolicy.CanWriteAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, type, id)
                : _scopePolicy.CanReadAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, type, id);

    private Task<bool> CanCreateAsync(IReadOnlyCollection<LabelDto> labels) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId) || !_tenantProvider.WorkspaceId.HasValue
            ? Task.FromResult(false)
            : _scopePolicy.CanCreateAsync(_tenantProvider.WorkspaceId.Value, _currentUser.UserId!, "server", labels);

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
        }).ToList(),
        OwnerUserId = server.OwnerUserId ?? string.Empty,
        EffectivePermission = LabelEffectivePermission.Owner,
        Capabilities = CatalogCapabilities.Owner
    };
}

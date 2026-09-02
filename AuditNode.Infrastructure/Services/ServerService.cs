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
    private readonly ILabelAccessService _labelAccess;
    private readonly ILabelMutationCoordinator _mutationCoordinator;
    private readonly ICurrentUserService _currentUser;
    private readonly IGlobalCatalogRepository _catalog;
    private readonly IOwnerLabelService _ownerLabels;
    private readonly TimeProvider _timeProvider;

    public ServerService(IServerRepository repository, ILabelAccessService labelAccess, ILabelMutationCoordinator mutationCoordinator, ICurrentUserService currentUser, IGlobalCatalogRepository catalog, IOwnerLabelService ownerLabels, TimeProvider timeProvider)
    {
        _repository = repository;
        _labelAccess = labelAccess;
        _mutationCoordinator = mutationCoordinator;
        _currentUser = currentUser;
        _catalog = catalog;
        _ownerLabels = ownerLabels;
        _timeProvider = timeProvider;
    }

    public Task<CursorPageDto<ServerResponseDto>> GetCatalogPageAsync(CatalogPageQuery query, string? ownerUserId = null, string? labelKey = null, string? labelValue = null, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult(new CursorPageDto<ServerResponseDto>([], null, false))
            : _catalog.GetServersAsync(_currentUser.UserId!, query, _timeProvider.GetUtcNow().UtcDateTime, ownerUserId, labelKey, labelValue, cancellationToken);

    public Task<ServerResponseDto?> GetCatalogDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult<ServerResponseDto?>(null)
            : _catalog.GetServerAsync(_currentUser.UserId!, id, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public Task<IReadOnlyList<ServerResponseDto>> ExportCatalogAsync(IReadOnlyCollection<Guid> ids, CatalogView view, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult<IReadOnlyList<ServerResponseDto>>([])
            : _catalog.ExportServersAsync(_currentUser.UserId!, view, ids, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<IEnumerable<ServerResponseDto>> GetServersAsync() =>
        (await GetCatalogPageAsync(new CatalogPageQuery(CatalogView.Mine, 100))).Items;

    public async Task<ServerResponseDto?> GetServerAsync(Guid id)
    {
        return id == Guid.Empty ? null : await GetCatalogDetailAsync(id);
    }

    public async Task<ServerOperationResult> CreateServerAsync(CreateServerDto dto)
    {
        var actor = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actor)) return new(ServerOperationStatus.Forbidden);

        if (!await _repository.DatacenterExistsAsync(dto.DatacenterId, actor))
            return new(ServerOperationStatus.DatacenterNotFound);

        var normalizedIp = NormalizeIp(dto.IpAddress);
        if (await _repository.IpAddressExistsAsync(normalizedIp, actor, null))
            return new(ServerOperationStatus.DuplicateIp);

        await _ownerLabels.EnsureAsync(actor);

        var server = new Server
        {
            Id = Guid.NewGuid(),
            OwnerUserId = actor,
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
        if (id == Guid.Empty)
            return new(ServerOperationStatus.NotFound);

        var server = await _repository.GetByIdAsync(id);
        if (server is null)
            return new(ServerOperationStatus.NotFound);
        var access = await _labelAccess.GetServerAccessAsync(id);
        if (access is null) return new(ServerOperationStatus.NotFound);
        if (!access.Capabilities.CanEditProperties) return new(ServerOperationStatus.Forbidden);
        if (dto.Labels is not null && !access.Capabilities.CanChangeLabels) return new(ServerOperationStatus.Forbidden);

        if (!await _repository.DatacenterExistsAsync(dto.DatacenterId, access.OwnerUserId))
            return new(ServerOperationStatus.DatacenterNotFound);

        var normalizedIp = NormalizeIp(dto.IpAddress);
        if (await _repository.IpAddressExistsAsync(normalizedIp, access.OwnerUserId, id))
            return new(ServerOperationStatus.DuplicateIp);

        try
        {
            var authorized = await _mutationCoordinator.ExecuteAsync(
                access.OwnerUserId,
                [id],
                [],
                async _ =>
                {
                    server.DatacenterId = dto.DatacenterId;
                    server.IpAddress = normalizedIp;
                    server.Hostname = dto.Hostname;
                    server.OsType = dto.OsType;
                    server.Environment = dto.Environment;
                    server.Status = dto.Status;
                    await _repository.UpdateAsync(server, dto.Labels);
                });
            if (!authorized) return new(ServerOperationStatus.Forbidden);
            return new(ServerOperationStatus.Success, Map(server));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return new(ServerOperationStatus.DuplicateIp);
        }
    }

    public async Task<ServerOperationStatus> PurgeServerAsync(Guid id)
    {
        if (id == Guid.Empty)
            return ServerOperationStatus.NotFound;

        var server = await _repository.GetByIdAsync(id);
        if (server is null)
            return ServerOperationStatus.NotFound;
        var access = await _labelAccess.GetServerAccessAsync(id);
        if (access is null) return ServerOperationStatus.NotFound;
        if (!access.Capabilities.CanDelete) return ServerOperationStatus.Forbidden;

        await _repository.DeleteAsync(server);
        return ServerOperationStatus.Success;
    }

    public async Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids) =>
        await ExportCatalogAsync(ids.Where(id => id != Guid.Empty).Distinct().ToList(), CatalogView.Mine);

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

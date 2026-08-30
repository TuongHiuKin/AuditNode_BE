using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IServerService
{
    Task<IEnumerable<ServerResponseDto>> GetServersAsync();
    Task<CursorPageDto<ServerResponseDto>> GetCatalogPageAsync(CatalogPageQuery query, CancellationToken cancellationToken = default);
    Task<ServerResponseDto?> GetServerAsync(Guid id);
    Task<ServerResponseDto?> GetCatalogDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ServerOperationResult> CreateServerAsync(CreateServerDto dto);
    Task<ServerOperationResult> UpdateServerAsync(Guid id, UpdateServerDto dto);
    Task<ServerOperationStatus> PurgeServerAsync(Guid id);
    Task<IEnumerable<ServerResponseDto>> ExportServersAsync(List<Guid> ids);
    Task<IReadOnlyList<ServerResponseDto>> ExportCatalogAsync(IReadOnlyCollection<Guid> ids, CatalogView view, CancellationToken cancellationToken = default);
}

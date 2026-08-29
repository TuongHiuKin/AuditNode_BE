using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ILabelAccessService
{
    Task<IReadOnlyList<Guid>> GetReadableServerIdsAsync(
        CatalogView view,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetReadableApplicationIdsAsync(
        CatalogView view,
        CancellationToken cancellationToken = default);

    Task<ResourceLabelAccessDto?> GetServerAccessAsync(
        Guid serverId,
        CancellationToken cancellationToken = default);

    Task<ResourceLabelAccessDto?> GetApplicationAccessAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default);
}

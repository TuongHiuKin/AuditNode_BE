using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IScopedResourcePolicy
{
    Task<bool> CanReadAsync(Guid workspaceId, string userId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default);
    Task<bool> CanWriteAsync(Guid workspaceId, string userId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default);
    Task<bool> CanCreateAsync(Guid workspaceId, string userId, string resourceType, IReadOnlyCollection<LabelDto> labels, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>?> GetReadableIdsAsync(Guid workspaceId, string userId, string resourceType, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>?> GetGrantedFrameIdsAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default);
}

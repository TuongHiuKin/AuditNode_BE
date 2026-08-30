using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IOwnerGraphAccessService
{
    Task<OwnerGraphAccessDto?> ResolveAsync(
        string ownerUserId,
        bool lockForWrite = false,
        CancellationToken cancellationToken = default);
}

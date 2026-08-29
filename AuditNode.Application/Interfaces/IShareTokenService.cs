using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IShareTokenService
{
    Task<ShareTokenMutationResult> CreateAsync(
        Guid labelId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    Task<ShareTokenResolutionDto?> ResolveAsync(
        string rawToken,
        CancellationToken cancellationToken = default);

    Task<ShareTokenMutationResult> RevokeAsync(
        Guid labelId,
        Guid grantId,
        long version,
        CancellationToken cancellationToken = default);
}

using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ILabelGrantService
{
    /// <param name="actingOwnerUserId">
    /// Whose catalog to act on. Null means the caller's own catalog. A different value is
    /// only accepted from a SystemAdmin, so an administrator can manage another user's
    /// sharing without gaining any access to that user's catalog rows.
    /// </param>
    Task<IReadOnlyList<LabelGrantDto>?> ListAsync(
        Guid labelId,
        string? actingOwnerUserId = null,
        CancellationToken cancellationToken = default);

    Task<LabelGrantMutationResult> CreateAsync(
        Guid labelId,
        CreateLabelGrantDto request,
        string? actingOwnerUserId = null,
        CancellationToken cancellationToken = default);

    Task<LabelGrantMutationResult> UpdateAsync(
        Guid labelId,
        Guid grantId,
        UpdateLabelGrantDto request,
        string? actingOwnerUserId = null,
        CancellationToken cancellationToken = default);

    Task<LabelGrantMutationResult> RevokeAsync(
        Guid labelId,
        Guid grantId,
        long version,
        string? actingOwnerUserId = null,
        CancellationToken cancellationToken = default);
}

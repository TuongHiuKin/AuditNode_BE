using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ILabelGrantService
{
    Task<IReadOnlyList<LabelGrantDto>?> ListAsync(
        Guid labelId,
        CancellationToken cancellationToken = default);

    Task<LabelGrantMutationResult> CreateAsync(
        Guid labelId,
        CreateLabelGrantDto request,
        CancellationToken cancellationToken = default);

    Task<LabelGrantMutationResult> UpdateAsync(
        Guid labelId,
        Guid grantId,
        UpdateLabelGrantDto request,
        CancellationToken cancellationToken = default);

    Task<LabelGrantMutationResult> RevokeAsync(
        Guid labelId,
        Guid grantId,
        long version,
        CancellationToken cancellationToken = default);
}

namespace AuditNode.Application.DTOs;

public sealed record CreateLabelGrantDto(
    string GranteeUserId,
    string Permission,
    DateTimeOffset? ExpiresAt);

public sealed record UpdateLabelGrantDto(
    string Permission,
    DateTimeOffset? ExpiresAt,
    long Version);

public sealed record LabelGrantDto(
    Guid Id,
    Guid LabelId,
    string GranteeUserId,
    string Permission,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    long Version,
    bool SharesAllOwnerResources = false,
    string? WarningCode = null);

public enum LabelGrantMutationStatus
{
    Success,
    Denied,
    Invalid,
    Conflict
}

public sealed record LabelGrantMutationResult(
    LabelGrantMutationStatus Status,
    LabelGrantDto? Grant = null);

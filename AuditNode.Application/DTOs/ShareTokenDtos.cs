namespace AuditNode.Application.DTOs;

public sealed record ShareTokenResolutionDto(
    Guid LabelId,
    string OwnerUserId,
    string Permission,
    bool SharesAllOwnerResources = false,
    string? WarningCode = null);

public enum ShareTokenMutationStatus
{
    Success,
    Denied,
    Invalid,
    Conflict
}

public sealed record ShareTokenMutationResult(
    ShareTokenMutationStatus Status,
    Guid? GrantId = null,
    string? RawToken = null,
    DateTimeOffset? ExpiresAt = null,
    long? Version = null,
    bool SharesAllOwnerResources = false,
    string? WarningCode = null);

public sealed record CreateShareLinkDto(DateTimeOffset ExpiresAt);

public sealed record ResolveShareLinkDto(string Token);

public sealed record CreateShareLinkResponseDto(
    Guid GrantId,
    string Token,
    DateTimeOffset ExpiresAt,
    long Version,
    bool SharesAllOwnerResources,
    string? WarningCode);

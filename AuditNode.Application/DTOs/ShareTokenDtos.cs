namespace AuditNode.Application.DTOs;

public sealed record ShareTokenResolutionDto(
    Guid LabelId,
    string OwnerUserId,
    string Permission);

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
    long? Version = null);

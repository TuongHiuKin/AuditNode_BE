namespace AuditNode.Application.DTOs;

public sealed record ShareTokenResolutionDto(
    Guid LabelId,
    string OwnerUserId,
    string Permission,
    bool SharesAllOwnerResources = false,
    string? WarningCode = null,
    Guid GrantId = default);

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

public sealed record BrowseShareLinkDto(
    string Token,
    string ResourceType,
    int? Limit = null,
    string? Cursor = null);

public sealed class ShareCatalogItemDto
{
    public string Type { get; set; } = string.Empty;
    public ServerResponseDto? Server { get; set; }
    public ApplicationResponseDto? Application { get; set; }
}

public sealed record CreateShareLinkResponseDto(
    Guid GrantId,
    string Token,
    DateTimeOffset ExpiresAt,
    long Version,
    bool SharesAllOwnerResources,
    string? WarningCode);

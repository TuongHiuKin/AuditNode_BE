namespace AuditNode.Application.DTOs;

public sealed record WorkspaceScopeTargetDto(Guid Id, string DisplayName);
public sealed record WorkspaceScopeDto(string Mode, IReadOnlyList<WorkspaceScopeTargetDto> Labels, IReadOnlyList<WorkspaceScopeTargetDto> Frames);
public sealed record WorkspaceCapabilitiesDto(bool CanManageShares, bool CanWriteInventory, bool CanEditGraph, bool CanManageDatacenters, bool CanManageLabels, bool CanImport);
public sealed record WorkspaceAccessDto(Guid WorkspaceId, string Relationship, string EffectiveRole, WorkspaceScopeDto Scope, WorkspaceCapabilitiesDto Capabilities);
public sealed record WorkspaceShareDto(string UserId, string Role, string ScopeMode, IReadOnlyList<Guid> TargetIds, long Version);
public sealed record UpsertWorkspaceShareDto(string UserId, string Role, string ScopeMode, IReadOnlyList<Guid> TargetIds, long Version = 0);
public sealed record WorkspaceShareResult(bool Success, string? ErrorCode = null, string? Error = null, WorkspaceShareDto? Share = null)
{
    public static WorkspaceShareResult Invalid(string error) => new(false, "invalid", error);
    public static WorkspaceShareResult Forbidden() => new(false, "forbidden", "Workspace share management is forbidden.");
    public static WorkspaceShareResult NotFound() => new(false, "not_found", "Workspace or member was not found.");
    public static WorkspaceShareResult Conflict(string error) => new(false, "conflict", error);
}

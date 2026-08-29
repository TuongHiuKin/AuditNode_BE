namespace AuditNode.Application.DTOs;

public enum CatalogView
{
    Mine,
    Shared
}

public enum LabelEffectivePermission
{
    Viewer,
    Editor,
    Owner
}

public sealed record LabelAccessCapabilities(
    bool CanRead,
    bool CanEditProperties,
    bool CanCreate,
    bool CanDelete,
    bool CanChangeLabels,
    bool CanChangeOwner,
    bool CanManageGrants);

public sealed record ResourceLabelAccessDto(
    Guid ResourceId,
    string OwnerUserId,
    LabelEffectivePermission EffectivePermission,
    IReadOnlyList<Guid> SharedLabelIds,
    LabelAccessCapabilities Capabilities);

using System.Text.Json.Serialization;

namespace AuditNode.Application.DTOs;

public enum CatalogView
{
    Mine,
    Shared
}

[JsonConverter(typeof(JsonStringEnumConverter<LabelEffectivePermission>))]
public enum LabelEffectivePermission
{
    [JsonStringEnumMemberName("viewer")]
    Viewer,
    [JsonStringEnumMemberName("editor")]
    Editor,
    [JsonStringEnumMemberName("owner")]
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

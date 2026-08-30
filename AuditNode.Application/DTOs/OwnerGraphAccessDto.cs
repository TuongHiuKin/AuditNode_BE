namespace AuditNode.Application.DTOs;

public sealed record OwnerGraphAccessDto(
    string OwnerUserId,
    LabelEffectivePermission EffectivePermission,
    IReadOnlySet<Guid> ReadableServerIds,
    IReadOnlySet<Guid> ReadableApplicationIds,
    IReadOnlySet<Guid> EditableServerIds,
    IReadOnlySet<Guid> EditableApplicationIds);

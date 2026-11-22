namespace AuditNode.Application.DTOs;

public static class LabelShareWarningCodes
{
    public const string OwnerLabelSharesAllOwnerResources = "owner_label_shares_all_owner_resources";
}

public sealed record LabelShareOptionUserDto(
    string Id,
    string Username,
    string? Email);

public sealed record LabelShareOptionsDto(
    IReadOnlyList<LabelShareOptionUserDto> Users,
    bool SharesAllOwnerResources,
    string? WarningCode);

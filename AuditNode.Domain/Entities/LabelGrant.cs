namespace AuditNode.Domain.Entities;

/// <summary>
/// Grants a registered user access to a label, or represents an anonymous viewer link.
/// Token-backed grants are never editor grants. Editors are selected AuditNode users and
/// are stored directly in <see cref="GranteeUserId"/>.
/// </summary>
public class LabelGrant
{
    public Guid Id { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public Guid LabelId { get; set; }
    public string? GranteeUserId { get; set; }
    public string Permission { get; set; } = LabelGrantPermissions.Viewer;
    public string? TokenHash { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long Version { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Label? Label { get; set; }
}

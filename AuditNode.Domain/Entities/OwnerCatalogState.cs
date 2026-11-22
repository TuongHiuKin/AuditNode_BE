namespace AuditNode.Domain.Entities;

/// <summary>
/// Provides the revision and row-lock boundary for one owner's catalog graph.
/// </summary>
public class OwnerCatalogState
{
    public string OwnerUserId { get; set; } = string.Empty;
    public long TopologyVersion { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

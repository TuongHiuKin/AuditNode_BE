namespace AuditNode.Domain.Entities;

/// <summary>
/// A directed dependency from an application to something it calls.
///
/// A dependency has exactly one destination, and the destination is either:
/// <list type="bullet">
/// <item>an internal deployment — <see cref="DestAppId"/> and <see cref="DestPortId"/> set; or</item>
/// <item>a third-party service — <see cref="ExternalServiceId"/> set.</item>
/// </list>
/// The invariant is enforced by the database check constraint
/// <c>ck_app_dependencies_single_destination</c> and by <see cref="IsInternal"/>.
/// </summary>
public class AppDependency
{
    public Guid Id { get; set; }
    public string? OwnerUserId { get; set; }
    public Guid SourceAppId { get; set; }

    /// <summary>Set when the dependency points at another application in this catalog.</summary>
    public Guid? DestAppId { get; set; }

    /// <summary>Set together with <see cref="DestAppId"/>; the specific port called.</summary>
    public Guid? DestPortId { get; set; }

    /// <summary>Set when the dependency points at a third-party service.</summary>
    public Guid? ExternalServiceId { get; set; }

    public string ConnectionType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True when this row targets another application rather than an external service.</summary>
    public bool IsInternal => DestAppId is not null;

    // Navigation properties
    public Application? SourceApplication { get; set; }
    public Application? DestinationApplication { get; set; }
    public PortMapping? DestinationPort { get; set; }
    public ExternalService? ExternalService { get; set; }
}

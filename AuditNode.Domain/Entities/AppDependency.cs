namespace AuditNode.Domain.Entities;

public class AppDependency
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string? OwnerUserId { get; set; }
    public Workspace? Workspace { get; set; }
    public Guid SourceAppId { get; set; }
    public Guid DestAppId { get; set; }
    public Guid DestPortId { get; set; }
    public string ConnectionType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Application? SourceApplication { get; set; }
    public Application? DestinationApplication { get; set; }
    public PortMapping? DestinationPort { get; set; }
}

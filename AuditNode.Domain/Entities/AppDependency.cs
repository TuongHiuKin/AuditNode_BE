namespace AuditNode.Domain.Entities;

public class AppDependency
{
    public Guid Id { get; set; }
    public Guid SourceAppId { get; set; }
    public Guid DestAppId { get; set; }
    public Guid DestPortId { get; set; }
    public string ConnectionType { get; set; } = string.Empty;

    // Navigation properties
    public Application? SourceApplication { get; set; }
    public Application? DestinationApplication { get; set; }
    public PortMapping? DestinationPort { get; set; }
}

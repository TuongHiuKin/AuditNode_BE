namespace AuditNode.Domain.Entities;

public class Application
{
    public Guid Id { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }

    // Navigation properties
    public ICollection<PortMapping> PortMappings { get; set; } = new List<PortMapping>();
    public ICollection<AppDependency> SourceDependencies { get; set; } = new List<AppDependency>();
    public ICollection<AppDependency> DestinationDependencies { get; set; } = new List<AppDependency>();
}

using AuditNode.Domain.Enums;

namespace AuditNode.Domain.Entities;

public class Application
{
    public Guid Id { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;
    public Guid ServerId { get; set; }

    // Navigation properties
    public Server? Server { get; set; }
    public ICollection<PortMapping> PortMappings { get; set; } = new List<PortMapping>();
    public ICollection<AppDependency> SourceDependencies { get; set; } = new List<AppDependency>();
    public ICollection<AppDependency> DestinationDependencies { get; set; } = new List<AppDependency>();
}

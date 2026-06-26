namespace AuditNode.Domain.Entities;

public class Application
{
    public Guid Id { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string OwnerTeam { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public virtual ICollection<Label> Labels { get; set; } = new List<Label>();

    // Navigation properties
    public ICollection<PortMapping> PortMappings { get; set; } = new List<PortMapping>();
    public ICollection<AppDependency> SourceDependencies { get; set; } = new List<AppDependency>();
    public ICollection<AppDependency> DestinationDependencies { get; set; } = new List<AppDependency>();
}

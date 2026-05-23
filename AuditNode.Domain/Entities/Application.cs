namespace AuditNode.Domain.Entities;

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public class Application
{
    public Guid Id { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public int PortNumber { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; }
    public Guid? TargetApplicationId { get; set; }
    public Guid ServerId { get; set; }

    // Navigation properties
    public Server? Server { get; set; }
    public Application? TargetApplication { get; set; }
    public ICollection<PortMapping> PortMappings { get; set; } = new List<PortMapping>();
    public ICollection<ApplicationDependency> Dependencies { get; set; } = new List<ApplicationDependency>();
    public ICollection<ApplicationDependency> Dependents { get; set; } = new List<ApplicationDependency>();
    public ICollection<AppDependency> SourceDependencies { get; set; } = new List<AppDependency>();
    public ICollection<AppDependency> DestinationDependencies { get; set; } = new List<AppDependency>();
}

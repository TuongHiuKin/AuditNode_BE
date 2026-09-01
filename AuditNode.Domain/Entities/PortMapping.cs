namespace AuditNode.Domain.Entities;

public class PortMapping
{
    public Guid Id { get; set; }
    public string? OwnerUserId { get; set; }
    public Guid ServerId { get; set; }
    public Guid AppId { get; set; }
    public int PortNumber { get; set; }
    public string Protocol { get; set; } = string.Empty;

    // Navigation properties
    public Server? Server { get; set; }
    public Application? Application { get; set; }
    public ICollection<AppDependency> AppDependencies { get; set; } = new List<AppDependency>();
}

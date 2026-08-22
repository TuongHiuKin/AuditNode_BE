namespace AuditNode.Domain.Entities;

public class Datacenter
{
    public Guid WorkspaceId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    // Navigation properties
    public Workspace? Workspace { get; set; }
    public ICollection<Server> Servers { get; set; } = new List<Server>();
}

namespace AuditNode.Domain.Entities;

public class Datacenter
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;

    // Navigation properties
    public ICollection<Server> Servers { get; set; } = new List<Server>();
}

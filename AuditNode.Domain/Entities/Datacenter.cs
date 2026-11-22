namespace AuditNode.Domain.Entities;

public class Datacenter
{
    public string? OwnerUserId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    // Navigation properties
    public ICollection<Server> Servers { get; set; } = new List<Server>();
}

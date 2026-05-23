namespace AuditNode.Domain.Entities;

public class Server
{
    public Guid Id { get; set; }
    public Guid DatacenterId { get; set; }
<<<<<<< Updated upstream
=======
    public Datacenter? DatacenterNavigation { get; set; }
>>>>>>> Stashed changes
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Datacenter { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    // Navigation properties
    public ICollection<PortMapping> PortMappings { get; set; } = new List<PortMapping>();
}

using System.ComponentModel.DataAnnotations.Schema;

namespace AuditNode.Domain.Entities;

public class Server
{
    public Guid Id { get; set; }
    public Guid DatacenterId { get; set; }
    public Datacenter? Datacenter { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    [Column("parent_frame_id")]
    public Guid? ParentFrameId { get; set; }
    public virtual BoundaryFrame? ParentFrame { get; set; }
    public virtual ICollection<Label> Labels { get; set; } = new List<Label>();

    // Navigation properties
    public ICollection<PortMapping> PortMappings { get; set; } = new List<PortMapping>();
}

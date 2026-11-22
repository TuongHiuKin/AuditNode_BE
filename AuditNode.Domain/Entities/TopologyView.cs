namespace AuditNode.Domain.Entities;

public class TopologyView
{
    public Guid ServerId { get; set; }
    public string ServerHostname { get; set; } = string.Empty;
    public string ServerIp { get; set; } = string.Empty;
    public Guid AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public int PortNumber { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public Guid DatacenterId { get; set; }
    public string? OwnerUserId { get; set; }
}

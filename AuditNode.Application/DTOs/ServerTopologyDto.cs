namespace AuditNode.Application.DTOs;

public class ServerTopologyDto
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Datacenter { get; set; } = string.Empty;
    public List<PortTopologyDto> Ports { get; set; } = new();
}

public class PortTopologyDto
{
    public Guid PortMappingId { get; set; }
    public Guid AppId { get; set; }
    public Guid ServerId { get; set; }
    public int PortNumber { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
}

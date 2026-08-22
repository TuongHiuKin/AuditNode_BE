namespace AuditNode.Application.DTOs;

public class ServerDetailDto
{
    public Guid Id { get; set; }
    public Guid DatacenterId { get; set; }
    public string DatacenterName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<ApplicationOnServerDto> Applications { get; set; } = new();
}

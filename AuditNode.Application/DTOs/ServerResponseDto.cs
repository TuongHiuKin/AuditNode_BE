namespace AuditNode.Application.DTOs;

public class ServerResponseDto
{
    public Guid Id { get; set; }
    public Guid DatacenterId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Datacenter { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<ApplicationOnServerDto> Applications { get; set; } = new();
}

public class ApplicationOnServerDto
{
    public Guid Id { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public int PortNumber { get; set; }
    public string Protocol { get; set; } = string.Empty;
}

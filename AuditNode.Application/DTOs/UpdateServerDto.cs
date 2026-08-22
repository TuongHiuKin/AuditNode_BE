namespace AuditNode.Application.DTOs;

public class UpdateServerDto
{
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid DatacenterId { get; set; }
    public List<LabelDto>? Labels { get; set; }
}

namespace AuditNode.Application.DTOs;

public class CreateServerDto
{
    public Guid DatacenterId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Datacenter { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public IEnumerable<LabelDto> Labels { get; set; } = new List<LabelDto>();
}

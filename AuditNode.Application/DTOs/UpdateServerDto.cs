namespace AuditNode.Application.DTOs;

public class UpdateServerDto
{
    public string Hostname { get; set; } = string.Empty;
    public string OsType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid DatacenterId { get; set; }
    public IEnumerable<LabelDto> Labels { get; set; } = new List<LabelDto>();
}

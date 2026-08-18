namespace AuditNode.Application.DTOs;

public class DeployedAppDto
{
    public Guid PortMappingId { get; set; }
    public Guid AppId { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public int PortNumber { get; set; }
}

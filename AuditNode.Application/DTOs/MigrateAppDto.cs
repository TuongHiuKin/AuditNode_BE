namespace AuditNode.Application.DTOs;

public class MigrateAppDto
{
    public Guid PortMappingId { get; set; }
    public Guid TargetServerId { get; set; }
    public int NewPortNumber { get; set; }
}

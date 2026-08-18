namespace AuditNode.Application.DTOs;

public class DependencyMapDto
{
    public List<ServerNodeDto> Servers { get; set; } = new();
    public List<ConnectionDto> Connections { get; set; } = new();
}

public class ConnectionDto
{
    public Guid Id { get; set; }
    public Guid SourceAppId { get; set; }
    public Guid TargetAppId { get; set; }
    public Guid DestinationPortMappingId { get; set; }
    public Guid DestinationServerId { get; set; }
    public string ConnectionType { get; set; } = string.Empty;
}

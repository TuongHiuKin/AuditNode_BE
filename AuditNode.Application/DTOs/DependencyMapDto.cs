namespace AuditNode.Application.DTOs;

public class DependencyMapDto
{
    public List<ServerNodeDto> Servers { get; set; } = new();
    public List<ConnectionDto> Connections { get; set; } = new();
}

public class ConnectionDto
{
    public Guid SourceAppId { get; set; }
    public Guid TargetAppId { get; set; }
}

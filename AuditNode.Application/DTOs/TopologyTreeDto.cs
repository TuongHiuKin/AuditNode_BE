namespace AuditNode.Application.DTOs;

public class TopologyTreeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public List<ServerNodeDto> Servers { get; set; } = new();
}

public class ServerNodeDto
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public List<ApplicationNodeDto> Applications { get; set; } = new();
}

public class ApplicationNodeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
}

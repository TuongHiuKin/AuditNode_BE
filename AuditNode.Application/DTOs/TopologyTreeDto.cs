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
    public string OsType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<ApplicationNodeDto> Applications { get; set; } = new();
    public List<TopologyLabelDto> Labels { get; set; } = new();
}

public class TopologyLabelDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
}

public class ApplicationNodeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid PortMappingId { get; set; }
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public List<TopologyLabelDto> Labels { get; set; } = new();
}

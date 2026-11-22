namespace AuditNode.Application.DTOs;

public class ApplicationResponseDto
{
    public Guid Id { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string OwnerTeam { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;
    public List<ServerOnApplicationDto> Servers { get; set; } = new();
    public List<LabelDto> Labels { get; set; } = new();
    public string OwnerUserId { get; set; } = string.Empty;
    public LabelEffectivePermission EffectivePermission { get; set; }
    public IReadOnlyList<Guid> SharedLabelIds { get; set; } = [];
    public LabelAccessCapabilities Capabilities { get; set; } = CatalogCapabilities.None;
}

public class ServerOnApplicationDto
{
    public Guid PortMappingId { get; set; }
    public Guid Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int PortNumber { get; set; }
    public string Protocol { get; set; } = string.Empty;
}

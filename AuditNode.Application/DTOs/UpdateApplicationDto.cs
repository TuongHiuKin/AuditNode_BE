using System.Text.Json.Serialization;

namespace AuditNode.Application.DTOs;

public class UpdateApplicationDto
{
    public string AppName { get; set; } = string.Empty;
    public string OwnerTeam { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;

    // Network Residency - Aligned with frontend JSON keys
    [JsonPropertyName("serverId")]
    public Guid? TargetServerId { get; set; }

    [JsonPropertyName("portNumber")]
    public int? PortNumber { get; set; }
    public IEnumerable<LabelDto> Labels { get; set; } = new List<LabelDto>();
}

using System.Text.Json.Serialization;

namespace AuditNode.Application.DTOs;

public class MigrateAppDto
{
    [JsonPropertyName("portMappingId")]
    public Guid PortMappingId { get; set; }

    [JsonPropertyName("serverId")]
    public Guid TargetServerId { get; set; }

    [JsonPropertyName("portNumber")]
    public int NewPortNumber { get; set; }
}

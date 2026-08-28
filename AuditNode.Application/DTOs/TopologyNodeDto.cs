using System.ComponentModel.DataAnnotations;

namespace AuditNode.Application.DTOs;

public class TopologyNodeDto
{
    public Guid Id { get; set; }
    public string NodeType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public Guid? ParentNodeId { get; set; }
    public Guid? ReferenceId { get; set; }
    public bool IsRestricted { get; set; }
}

public class TopologyEdgeDto
{
    public Guid Id { get; set; }
    public Guid SourceNodeId { get; set; }
    public Guid TargetNodeId { get; set; }
    public string SourceHandle { get; set; } = string.Empty;
    public string TargetHandle { get; set; } = string.Empty;
    public string EdgeType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
}

public class TopologyStateDto
{
    public long Version { get; set; }
    public List<TopologyNodeDto> Nodes { get; set; } = new();
    public List<TopologyEdgeDto> Edges { get; set; } = new();
}

public sealed class SaveTopologyStateDto
{
    [Required]
    public long? Version { get; set; }

    [Required]
    public List<TopologyNodeDto>? Nodes { get; set; }

    [Required]
    public List<TopologyEdgeDto>? Edges { get; set; }

    [Required]
    public List<DependencyItemDto>? Dependencies { get; set; }
}

public sealed record TopologyCommandBatchDto(
    [property: Required] long? Version,
    [property: Required] IReadOnlyList<TopologyCommandDto?> Operations);

public sealed record TopologyCommandDto
{
    public string Type { get; init; } = string.Empty;
    public Guid? NodeId { get; init; }
    public Guid? ParentId { get; init; }
    public Guid? EdgeId { get; init; }
    public Guid? SourceNodeId { get; init; }
    public Guid? TargetNodeId { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public string? SourceHandle { get; init; }
    public string? TargetHandle { get; init; }
    public string? EdgeType { get; init; }
    public string? Label { get; init; }
}

public enum TopologyCommandStatus
{
    Success,
    InvalidRequest,
    Forbidden,
    Conflict
}

public sealed record TopologyCommandResult(TopologyCommandStatus Status, long Version, string? Error = null);

public enum TopologyStateStatus
{
    Success,
    InvalidRequest,
    DuplicateId,
    InvalidParent,
    InvalidReference,
    InvalidEdge,
    InvalidDependency,
    Forbidden,
    Conflict
}

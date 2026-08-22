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
    public List<TopologyNodeDto> Nodes { get; set; } = new();
    public List<TopologyEdgeDto> Edges { get; set; } = new();
}

public class SaveTopologyStateDto : TopologyStateDto;

public enum TopologyStateStatus
{
    Success,
    InvalidRequest,
    DuplicateId,
    InvalidParent,
    InvalidReference,
    InvalidEdge
}

namespace AuditNode.Application.DTOs;

public class DependencyMapDto
{
    public List<NodeDto> Nodes { get; set; } = new();
    public List<EdgeDto> Edges { get; set; } = new();
}

public class NodeDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "default";
    public NodeDataDto Data { get; set; } = new();
    public PositionDto Position { get; set; } = new();
}

public class NodeDataDto
{
    public string Label { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
}

public class PositionDto
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class EdgeDto
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? Label { get; set; }
}

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

public class SaveTopologyStateDto
{
    public List<TopologyNodeDto> Nodes { get; set; } = new();
}

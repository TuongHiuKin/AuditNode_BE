namespace AuditNode.Domain.Entities;

public class TopologyNode
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    
    public string NodeType { get; set; } = string.Empty; // 'server', 'application', 'group'
    public string Label { get; set; } = string.Empty;
    
    public double X { get; set; }
    public double Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    // Self-referencing relationship for grouping
    public Guid? ParentNodeId { get; set; }
    public TopologyNode? ParentNode { get; set; }
    public ICollection<TopologyNode> ChildNodes { get; set; } = new List<TopologyNode>();

    // Optional reference to Server or Application ID
    public Guid? ReferenceId { get; set; }
}

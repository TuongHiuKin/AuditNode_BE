namespace AuditNode.Domain.Entities;

public class TopologyEdge
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string? OwnerUserId { get; set; }
    public Workspace? Workspace { get; set; }
    public Guid SourceNodeId { get; set; }
    public TopologyNode? SourceNode { get; set; }
    public Guid TargetNodeId { get; set; }
    public TopologyNode? TargetNode { get; set; }
    public string SourceHandle { get; set; } = string.Empty;
    public string TargetHandle { get; set; } = string.Empty;
    public string EdgeType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
}

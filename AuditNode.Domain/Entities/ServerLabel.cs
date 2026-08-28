namespace AuditNode.Domain.Entities;

public class ServerLabel
{
    public Guid WorkspaceId { get; set; }
    public string? OwnerUserId { get; set; }
    public Guid ServerId { get; set; }
    public Guid LabelId { get; set; }

    public Workspace? Workspace { get; set; }
    public Server? Server { get; set; }
    public Label? Label { get; set; }
}

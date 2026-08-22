namespace AuditNode.Domain.Entities;

public class ApplicationLabel
{
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid LabelId { get; set; }

    public Workspace? Workspace { get; set; }
    public Application? Application { get; set; }
    public Label? Label { get; set; }
}

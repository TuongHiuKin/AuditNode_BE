namespace AuditNode.Domain.Entities;

public class ApplicationLabel
{
    public string? OwnerUserId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid LabelId { get; set; }

    public Application? Application { get; set; }
    public Label? Label { get; set; }
}

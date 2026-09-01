namespace AuditNode.Domain.Entities;

public class ServerLabel
{
    public string? OwnerUserId { get; set; }
    public Guid ServerId { get; set; }
    public Guid LabelId { get; set; }

    public Server? Server { get; set; }
    public Label? Label { get; set; }
}

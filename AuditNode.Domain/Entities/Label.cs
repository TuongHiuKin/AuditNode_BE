namespace AuditNode.Domain.Entities;

public class Label
{
    public string? OwnerUserId { get; set; }
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Kind { get; set; } = LabelKinds.Business;
    public bool IsProtected { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Server> Servers { get; set; } = new List<Server>();
    public ICollection<ServerLabel> ServerLabels { get; set; } = new List<ServerLabel>();
    public ICollection<ApplicationLabel> ApplicationLabels { get; set; } = new List<ApplicationLabel>();
    public ICollection<LabelGrant> Grants { get; set; } = new List<LabelGrant>();
}

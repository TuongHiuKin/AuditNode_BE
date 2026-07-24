namespace AuditNode.Domain.Entities;

public class Label
{
    public Guid WorkspaceId { get; set; }
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public ICollection<Server> Servers { get; set; } = new List<Server>();
}

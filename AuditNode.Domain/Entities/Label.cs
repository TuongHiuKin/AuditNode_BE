namespace AuditNode.Domain.Entities;

public class Label
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
}

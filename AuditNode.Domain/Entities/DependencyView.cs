namespace AuditNode.Domain.Entities;

public class DependencyView
{
    public Guid SourceAppId { get; set; }
    public string SourceAppName { get; set; } = string.Empty;
    public string SourceAppCode { get; set; } = string.Empty;
    public Guid DestAppId { get; set; }
    public string DestAppName { get; set; } = string.Empty;
    public string DestAppCode { get; set; } = string.Empty;
    public int DestPortNumber { get; set; }
    public string ConnectionType { get; set; } = string.Empty;
    public string DestServerHostname { get; set; } = string.Empty;
    public string? Environment { get; set; }
    public Guid? DatacenterId { get; set; }
    public string? OwnerUserId { get; set; }
}

namespace AuditNode.Domain.Entities;

public class ApplicationDependency
{
    public Guid SourceAppId { get; set; }
    public Application? SourceApp { get; set; }

    public Guid TargetAppId { get; set; }
    public Application? TargetApp { get; set; }
}

using AuditNode.Domain.Enums;

namespace AuditNode.Application.DTOs;

public class CreateApplicationDto
{
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public int PortNumber { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; }
    public Guid? TargetApplicationId { get; set; }
    public Guid ServerId { get; set; }
}

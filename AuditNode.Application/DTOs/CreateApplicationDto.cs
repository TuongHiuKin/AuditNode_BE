namespace AuditNode.Application.DTOs;

public class CreateApplicationDto
{
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
<<<<<<< Updated upstream
    public Guid OwnerId { get; set; }
=======
    public string OwnerId { get; set; } = string.Empty;
    public int PortNumber { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; }
    public Guid? TargetApplicationId { get; set; }
    public Guid ServerId { get; set; }
>>>>>>> Stashed changes
}

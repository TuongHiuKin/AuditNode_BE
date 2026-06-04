namespace AuditNode.Application.DTOs;

public class UpdateApplicationDto
{
    public string AppName { get; set; } = string.Empty;
    public string OwnerTeam { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;
}

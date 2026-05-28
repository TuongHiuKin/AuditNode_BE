namespace AuditNode.Application.DTOs;

public class CreateApplicationDto
{
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string OwnerTeam { get; set; } = string.Empty;
    public int PortNumber { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string? Risk { get; set; }
    public string? Icon { get; set; }
    public string? TechStack { get; set; }
    public Guid ServerId { get; set; }
}

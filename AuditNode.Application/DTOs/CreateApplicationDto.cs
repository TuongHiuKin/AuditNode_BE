namespace AuditNode.Application.DTOs;

public class CreateApplicationDto
{
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string OwnerTeam { get; set; } = string.Empty;
    public string? Risk { get; set; }
    public string? Icon { get; set; }
    public string? TechStack { get; set; }
    public IEnumerable<LabelDto> Labels { get; set; } = new List<LabelDto>();
}

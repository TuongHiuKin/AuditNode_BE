namespace AuditNode.Application.DTOs;

public class CreateApplicationDto
{
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
}

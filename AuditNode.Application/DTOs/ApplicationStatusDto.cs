namespace AuditNode.Application.DTOs;

public class ApplicationStatusDto
{
    public Guid Id { get; set; }
    public string AppName { get; set; } = string.Empty;
    public bool IsMapped { get; set; } // True if already on canvas, False if available for Palette
}

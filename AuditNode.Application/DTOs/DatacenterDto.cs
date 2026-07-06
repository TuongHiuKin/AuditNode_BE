namespace AuditNode.Application.DTOs;

public class DatacenterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}

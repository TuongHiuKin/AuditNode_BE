namespace AuditNode.Application.DTOs;

public class ImportResponseDto
{
    public int TotalProcessed { get; set; }
    public int SavedCount { get; set; }
    public List<ImportErrorDto> Errors { get; set; } = new List<ImportErrorDto>();
    public List<ImportConflictDto> Conflicts { get; set; } = new List<ImportConflictDto>();
}

public class ImportErrorDto
{
    public int Row { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ImportConflictDto
{
    public int Row { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

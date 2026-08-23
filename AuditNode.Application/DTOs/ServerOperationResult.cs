namespace AuditNode.Application.DTOs;

public enum ServerOperationStatus
{
    Success,
    InvalidWorkspace,
    NotFound,
    DatacenterNotFound,
    DuplicateIp
    ,Forbidden
}

public sealed record ServerOperationResult(
    ServerOperationStatus Status,
    ServerResponseDto? Server = null);

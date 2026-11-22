namespace AuditNode.Application.DTOs;

public enum ApplicationOperationStatus
{
    Success,
    InvalidRequest,
    NotFound,
    ServerNotFound,
    DeploymentNotFound,
    DuplicateAppCode,
    PortCollision,
    Forbidden
}

public sealed record ApplicationOperationResult(
    ApplicationOperationStatus Status,
    ApplicationResponseDto? Application = null);

public enum DeploymentOperationStatus
{
    Success,
    InvalidRequest,
    NotFound,
    ServerNotFound,
    PortCollision,
    Forbidden
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AuditNode.Application.DTOs;

public class SyncDependenciesDto
{
    [Required]
    public long? Version { get; set; }

    [Required]
    public List<DependencyItemDto>? Dependencies { get; set; }
}

public class DependencyItemDto
{
    public Guid SourceAppId { get; set; }
    public Guid DestAppId { get; set; }
    public Guid DestinationPortMappingId { get; set; }
}

public enum DependencySyncStatus
{
    Success,
    InvalidRequest,
    NotFound,
    SelfLoop,
    Duplicate,
    DestinationMismatch,
    Forbidden,
    Conflict
}

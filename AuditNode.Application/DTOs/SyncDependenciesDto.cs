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

    /// <summary>Set for an internal destination; null when the dependency is external.</summary>
    public Guid? DestAppId { get; set; }

    /// <summary>Set together with <see cref="DestAppId"/>.</summary>
    public Guid? DestinationPortMappingId { get; set; }

    /// <summary>Set for a third-party destination; null when the dependency is internal.</summary>
    public Guid? ExternalServiceId { get; set; }
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

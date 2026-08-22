using System;
using System.Collections.Generic;

namespace AuditNode.Application.DTOs;

public class SyncDependenciesDto
{
    public List<DependencyItemDto> Dependencies { get; set; } = new();
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
    DestinationMismatch
}

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
}

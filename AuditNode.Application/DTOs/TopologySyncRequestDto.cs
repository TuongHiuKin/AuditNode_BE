using System;
using System.Collections.Generic;

namespace AuditNode.Application.DTOs;

public record TopologySyncRequestDto
{
    public List<FrameSyncDto> Frames { get; init; } = new();
    public List<NodeAssignmentDto> Assignments { get; init; } = new();
}

public record FrameSyncDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public record NodeAssignmentDto
{
    public Guid NodeId { get; init; }
    public string? ParentFrameId { get; init; }
}

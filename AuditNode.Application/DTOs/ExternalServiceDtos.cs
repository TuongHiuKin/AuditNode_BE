namespace AuditNode.Application.DTOs;

/// <summary>Read model for a third-party dependency.</summary>
public class ExternalServiceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? Provider { get; set; }
    public string? Protocol { get; set; }
    public int? Port { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>How many applications currently depend on this service.</summary>
    public int DependentApplicationCount { get; set; }
}

/// <summary>Write model. Endpoint/Provider/Protocol/Port/Notes are optional.</summary>
public class CreateExternalServiceDto
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? Provider { get; set; }
    public string? Protocol { get; set; }
    public int? Port { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

/// <summary>Unchanged fields keep their stored value when left null.</summary>
public class UpdateExternalServiceDto
{
    public string? Name { get; set; }
    public string? Kind { get; set; }
    public string? Endpoint { get; set; }
    public string? Provider { get; set; }
    public string? Protocol { get; set; }
    public int? Port { get; set; }
    public string? Environment { get; set; }
    public string? Notes { get; set; }
}

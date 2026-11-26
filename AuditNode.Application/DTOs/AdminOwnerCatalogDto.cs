namespace AuditNode.Application.DTOs;

/// <summary>
/// Read-only snapshot of another owner's catalog, for SystemAdmin inspection.
///
/// Deliberately carries no tenant-mutable entity: this DTO exists so an
/// administrator can see what a user has recorded and nothing more.
/// </summary>
public class AdminOwnerCatalogDto
{
    public string OwnerUserId { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Email { get; set; }

    public List<AdminCatalogServerDto> Servers { get; set; } = new();
    public List<AdminCatalogApplicationDto> Applications { get; set; } = new();
    public List<AdminCatalogExternalServiceDto> ExternalServices { get; set; } = new();
    public List<AdminCatalogDependencyDto> Dependencies { get; set; } = new();

    /// <summary>Labels the owner can share, with their current grant count.</summary>
    public List<AdminCatalogLabelDto> Labels { get; set; } = new();
}

public class AdminCatalogServerDto
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string OsType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Datacenter { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
}

public class AdminCatalogApplicationDto
{
    public Guid Id { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string OwnerTeam { get; set; } = string.Empty;
    public string TechStack { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public List<AdminCatalogPortDto> Ports { get; set; } = new();
    public List<string> Labels { get; set; } = new();
}

public class AdminCatalogPortDto
{
    public Guid Id { get; set; }
    public int PortNumber { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string ServerHostname { get; set; } = string.Empty;
}

public class AdminCatalogExternalServiceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? Provider { get; set; }
    public string Environment { get; set; } = string.Empty;
}

public class AdminCatalogDependencyDto
{
    public Guid Id { get; set; }
    public string SourceAppCode { get; set; } = string.Empty;

    /// <summary>Set for an internal dependency.</summary>
    public string? DestinationAppCode { get; set; }

    /// <summary>Set for a third-party dependency.</summary>
    public string? ExternalServiceName { get; set; }
}

public class AdminCatalogLabelDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;

    /// <summary>Active grants on this label.</summary>
    public int ActiveGrantCount { get; set; }
}

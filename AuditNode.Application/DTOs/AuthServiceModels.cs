namespace AuditNode.Application.DTOs;

public sealed record IdentityTokenSet(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    int RefreshExpiresIn);

public sealed class IdentityAuthenticationException : Exception
{
    public IdentityAuthenticationException() : base("Authentication failed.") { }
}

public sealed class IdentityConflictException : Exception
{
    public IdentityConflictException() : base("The identity already exists.") { }
}

public sealed class IdentityConfigurationException : Exception
{
    public IdentityConfigurationException() : base("Identity service configuration is invalid.") { }
}

public sealed class IdentityUpstreamUnavailableException : Exception
{
    public IdentityUpstreamUnavailableException() : base("Identity service is unavailable.") { }
}

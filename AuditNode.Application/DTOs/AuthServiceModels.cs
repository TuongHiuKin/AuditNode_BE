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

public sealed class IdentityMutationLockUnavailableException : Exception
{
    public IdentityMutationLockUnavailableException() : base("Identity administration is busy.") { }
    public IdentityMutationLockUnavailableException(Exception innerException) : base("Identity administration is busy.", innerException) { }
}

public sealed class IdentityInvariantViolationException : Exception
{
    public IdentityInvariantViolationException() : base("The enabled SystemAdmin invariant could not be verified.") { }
}

public sealed class IdentityProtectedException : Exception
{
    public IdentityProtectedException() : base("The protected identity cannot be modified through AuditNode.") { }
}

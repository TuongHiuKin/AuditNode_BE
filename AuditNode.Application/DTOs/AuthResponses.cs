namespace AuditNode.Application.DTOs;

public sealed class AuthenticationResponseDto
{
    public required string AccessToken { get; init; }
    public int ExpiresIn { get; init; }
}

public sealed class RefreshResponseDto
{
    public required string AccessToken { get; init; }
    public int ExpiresIn { get; init; }
}

public sealed class CurrentUserDto
{
    public string Id { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string? Email { get; init; }
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();
}

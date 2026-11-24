namespace AuditNode.Infrastructure.Identity;

/// <summary>
/// Auth settings for locally issued tokens. Bound from the <c>Identity</c>
/// configuration section and validated at startup, so a deployment cannot run with
/// a missing or too-short signing key.
/// </summary>
public sealed class IdentityRuntimeOptions
{
    /// <summary>HMAC-SHA256 needs at least 32 bytes to be a sound key.</summary>
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = "AuditNode";
    public string Audience { get; set; } = "auditnode-api";
    public string? SigningKey { get; set; }

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Issuer)
        && !string.IsNullOrWhiteSpace(Audience)
        && !string.IsNullOrWhiteSpace(SigningKey)
        && System.Text.Encoding.UTF8.GetByteCount(SigningKey) >= MinimumSigningKeyBytes;
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AuditNode.API.IntegrationTests.Fixtures;

/// <summary>
/// Mints tokens the API accepts. The API now signs its own access tokens with a
/// symmetric key (<c>Identity:SigningKey</c>), so tests share that key rather than
/// publishing an RSA key through OpenID metadata.
/// </summary>
public sealed class TestJwtIssuer : IDisposable
{
    public const string Issuer = "https://issuer.auditnode.test";
    public const string Audience = "auditnode-api";
    public const string SigningKey = "integration-test-signing-key-at-least-32-bytes-long";

    public SecurityKey SecurityKey { get; } =
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));

    public string CreateToken(
        string subject = "test-user",
        string? audience = Audience,
        string? authorizedParty = null,
        string issuer = Issuer,
        DateTime? expires = null,
        bool systemAdmin = false)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject),
            new(JwtRegisteredClaimNames.UniqueName, subject),
        };

        if (systemAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "SystemAdmin"));

        var tokenExpires = expires ?? now.AddMinutes(5);
        var notBefore = tokenExpires <= now ? tokenExpires.AddMinutes(-5) : now.AddMinutes(-1);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore,
            tokenExpires,
            new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose()
    {
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace AuditNode.API.IntegrationTests.Fixtures;

public sealed class TestJwtIssuer : IDisposable
{
    public const string Issuer = "https://issuer.auditnode.test";
    public const string Audience = "auditnode-api";
    public const string AuthorizedParty = "auditnode-bff";

    private readonly RSA _rsa = RSA.Create(2048);

    public SecurityKey SecurityKey { get; }

    public TestJwtIssuer()
    {
        SecurityKey = new RsaSecurityKey(_rsa) { KeyId = "auditnode-api-integration-key" };
    }

    public string CreateToken(
        string subject = "test-user",
        string? audience = Audience,
        string? authorizedParty = AuthorizedParty,
        string issuer = Issuer,
        DateTime? expires = null,
        bool systemAdmin = false)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new("preferred_username", subject),
            new("realm_access", JsonSerializer.Serialize(new
            {
                roles = systemAdmin ? new[] { "SystemAdmin" } : Array.Empty<string>()
            }), JsonClaimValueTypes.Json)
        };

        if (authorizedParty is not null)
            claims.Add(new Claim("azp", authorizedParty));

        var tokenExpires = expires ?? now.AddMinutes(5);
        var notBefore = tokenExpires <= now ? tokenExpires.AddMinutes(-5) : now.AddMinutes(-1);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore,
            tokenExpires,
            new SigningCredentials(SecurityKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() => _rsa.Dispose();
}

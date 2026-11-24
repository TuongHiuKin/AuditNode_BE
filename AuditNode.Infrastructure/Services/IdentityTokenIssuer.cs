using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AuditNode.Infrastructure.Services;

/// <summary>
/// Issues self-signed access JWTs. The signing key comes from
/// <c>Identity:SigningKey</c>; startup fails fast when it is missing so a
/// deployment cannot silently run with a weak or empty key.
/// </summary>
public sealed class IdentityTokenIssuer(IConfiguration configuration, TimeProvider timeProvider) : IIdentityTokenIssuer
{
    private const int AccessTokenLifetimeSeconds = 900;

    public AccessTokenIssue CreateAccessToken(string userId, string username, string? email, IEnumerable<string> roles)
    {
        var key = configuration["Identity:SigningKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new IdentityConfigurationException();

        var issuer = configuration["Identity:Issuer"] ?? "AuditNode";
        var audience = configuration["Identity:Audience"] ?? "auditnode-api";
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
        }
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(AccessTokenLifetimeSeconds),
            signingCredentials: credentials);

        return new AccessTokenIssue(new JwtSecurityTokenHandler().WriteToken(token), AccessTokenLifetimeSeconds);
    }
}

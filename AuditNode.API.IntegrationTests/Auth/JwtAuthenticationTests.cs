using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using AuditNode.API.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;

namespace AuditNode.API.IntegrationTests.Auth;

[Trait("Category", "ApiIntegration")]
public sealed class JwtAuthenticationTests(AuditNodeWebApplicationFactory factory)
    : IClassFixture<AuditNodeWebApplicationFactory>
{
    [Fact]
    public async Task Protected_route_without_token_returns_unauthorized()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_route_with_valid_api_token_returns_ok()
    {
        using var client = ClientWith(factory.Tokens.CreateToken());

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Protected_route_with_wrong_audience_returns_unauthorized()
    {
        using var client = ClientWith(factory.Tokens.CreateToken(audience: "another-api"));

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_route_with_token_issued_by_another_key_returns_unauthorized()
    {
        // A token signed with a different key must not be accepted, even when every
        // other claim (issuer, audience, expiry) matches.
        var foreignKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            "a-completely-different-signing-key-32-bytes!"));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            TestJwtIssuer.Issuer,
            TestJwtIssuer.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, "forged-user")],
            now.AddMinutes(-1),
            now.AddMinutes(5),
            new SigningCredentials(foreignKey, SecurityAlgorithms.HmacSha256));
        using var client = ClientWith(new JwtSecurityTokenHandler().WriteToken(token));

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SystemAdmin_route_without_the_role_returns_forbidden()
    {
        // Authorisation now comes from role claims on the locally signed token, so a
        // valid token that lacks SystemAdmin must still be refused.
        using var client = ClientWith(factory.Tokens.CreateToken(systemAdmin: false));

        var response = await client.GetAsync("/api/v1/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SystemAdmin_route_with_the_role_returns_ok()
    {
        using var client = ClientWith(factory.Tokens.CreateToken(systemAdmin: true));

        var response = await client.GetAsync("/api/v1/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Protected_route_with_wrong_issuer_returns_unauthorized()
    {
        using var client = ClientWith(factory.Tokens.CreateToken(issuer: "https://untrusted-issuer.test"));

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_route_with_expired_token_returns_unauthorized()
    {
        using var client = ClientWith(factory.Tokens.CreateToken(expires: DateTime.UtcNow.AddMinutes(-10)));

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_route_with_tampered_signature_returns_unauthorized()
    {
        var token = factory.Tokens.CreateToken();
        var segments = token.Split('.');
        segments[2] = (segments[2][0] == 'A' ? 'B' : 'A') + segments[2][1..];
        var tampered = string.Join('.', segments);
        using var client = ClientWith(tampered);

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient ClientWith(string token)
    {
        var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

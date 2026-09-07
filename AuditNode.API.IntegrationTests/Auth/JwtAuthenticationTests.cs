using System.Net;
using System.Net.Http.Headers;
using AuditNode.API.IntegrationTests.Fixtures;
using FluentAssertions;

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
    public async Task Protected_route_with_wrong_authorized_party_returns_unauthorized()
    {
        using var client = ClientWith(factory.Tokens.CreateToken(authorizedParty: "another-client"));

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_route_with_missing_authorized_party_returns_unauthorized()
    {
        using var client = ClientWith(factory.Tokens.CreateToken(authorizedParty: null));

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

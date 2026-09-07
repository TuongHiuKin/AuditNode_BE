using System.Net;
using System.Text;
using AuditNode.API.IntegrationTests.Fixtures;
using FluentAssertions;

namespace AuditNode.API.IntegrationTests.Auth;

[Trait("Category", "ApiIntegration")]
public sealed class SessionLifecycleTests(AuditNodeWebApplicationFactory factory)
    : IClassFixture<AuditNodeWebApplicationFactory>
{
    [Fact]
    public async Task Refresh_without_cookie_returns_unauthorized()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsync("/api/v1/auth/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoked_refresh_cookie_returns_unauthorized_and_expires_local_cookie()
    {
        factory.Identities.RejectRefreshToken = true;
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", "auditnode.refresh_token=revoked-token");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Single().Should().Contain("auditnode.refresh_token=")
            .And.Contain("expires=Thu, 01 Jan 1970 00:00:00 GMT");
    }

    [Fact]
    public async Task Login_response_sets_secure_http_only_refresh_cookie()
    {
        using var client = factory.CreateHttpsClient();
        using var content = new StringContent(
            "{\"username\":\"tester\",\"password\":\"valid-password\"}",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.Should().Contain("auditnode.refresh_token=")
            .And.Contain("httponly")
            .And.Contain("secure")
            .And.Contain("samesite=none")
            .And.Contain("path=/api/v1/auth", Exactly.Once());
    }
}

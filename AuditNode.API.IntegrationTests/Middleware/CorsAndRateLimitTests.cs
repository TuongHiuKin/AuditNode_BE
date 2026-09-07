using System.Net;
using System.Text;
using System.Net.Http.Headers;
using AuditNode.API.IntegrationTests.Fixtures;
using FluentAssertions;

namespace AuditNode.API.IntegrationTests.Middleware;

[Trait("Category", "ApiIntegration")]
public sealed class CorsAndRateLimitTests(AuditNodeWebApplicationFactory factory)
    : IClassFixture<AuditNodeWebApplicationFactory>
{
    [Fact]
    public async Task Allowed_origin_preflight_returns_cors_headers()
    {
        using var client = factory.CreateHttpsClient();
        using var request = Preflight("https://allowed.auditnode.test");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://allowed.auditnode.test");
        response.Headers.GetValues("Access-Control-Allow-Credentials").Should().ContainSingle("true");
    }

    [Fact]
    public async Task Disallowed_origin_preflight_does_not_return_cors_permission()
    {
        using var client = factory.CreateHttpsClient();
        using var request = Preflight("https://evil.example");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Auth_policy_rejects_request_after_permit_limit()
    {
        using var client = factory.CreateHttpsClient();
        var statuses = new List<HttpStatusCode>();

        for (var index = 0; index < 21; index++)
        {
            using var content = new StringContent(
                "{\"username\":\"tester\",\"password\":\"valid-password\"}",
                Encoding.UTF8,
                "application/json");
            statuses.Add((await client.PostAsync("/api/v1/auth/login", content)).StatusCode);
        }

        statuses.Take(20).Should().OnlyContain(status => status == HttpStatusCode.OK);
        statuses.Last().Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Share_options_policy_rejects_request_after_permit_limit()
    {
        var labelId = Guid.NewGuid();
        using var client = AuthorizedClient($"share-options-{Guid.NewGuid():N}");

        await AssertLimited(
            client,
            30,
            () => new HttpRequestMessage(HttpMethod.Get, $"/api/v1/labels/{labelId}/share-options?search=test"));
    }

    [Fact]
    public async Task Share_link_create_policy_rejects_request_after_permit_limit()
    {
        var labelId = Guid.NewGuid();
        using var client = AuthorizedClient($"share-create-{Guid.NewGuid():N}");

        await AssertLimited(client, 10, () => JsonPost(
            $"/api/v1/labels/{labelId}/share-links",
            $"{{\"expiresAt\":\"{DateTimeOffset.UtcNow.AddHours(1):O}\"}}"));
    }

    [Fact]
    public async Task Share_link_resolve_policy_rejects_request_after_permit_limit()
    {
        using var client = factory.CreateHttpsClient();

        await AssertLimited(client, 30, () => JsonPost(
            "/api/v1/share-links/resolve",
            "{\"token\":\"invalid-test-token\"}"));
    }

    [Fact]
    public async Task Share_link_browse_policy_rejects_request_after_permit_limit()
    {
        using var client = factory.CreateHttpsClient();

        await AssertLimited(client, 30, () => JsonPost(
            "/api/v1/share-links/browse",
            "{\"token\":\"invalid-test-token\",\"resourceType\":\"server\",\"limit\":10}"));
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/servers");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return request;
    }

    private HttpClient AuthorizedClient(string subject)
    {
        var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.Tokens.CreateToken(subject: subject));
        return client;
    }

    private static HttpRequestMessage JsonPost(string path, string json) => new(HttpMethod.Post, path)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static async Task AssertLimited(
        HttpClient client,
        int permitLimit,
        Func<HttpRequestMessage> requestFactory)
    {
        for (var index = 0; index < permitLimit; index++)
        {
            using var request = requestFactory();
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"request {index + 1} is still within the configured permit limit");
        }

        using var rejectedRequest = requestFactory();
        using var rejected = await client.SendAsync(rejectedRequest);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

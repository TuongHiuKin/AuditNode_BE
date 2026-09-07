using System.Net;
using System.Net.Http.Headers;
using AuditNode.API.IntegrationTests.Fixtures;
using FluentAssertions;

namespace AuditNode.API.IntegrationTests.Authorization;

[Trait("Category", "ApiIntegration")]
public sealed class SystemAdminAuthorizationTests(AuditNodeWebApplicationFactory factory)
    : IClassFixture<AuditNodeWebApplicationFactory>
{
    [Fact]
    public async Task Normal_user_cannot_list_admin_users()
    {
        using var client = Authorized(factory.Tokens.CreateToken());

        var response = await client.GetAsync("/api/v1/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SystemAdmin_can_list_admin_users()
    {
        using var client = Authorized(factory.Tokens.CreateToken(systemAdmin: true));

        var response = await client.GetAsync("/api/v1/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient Authorized(string token)
    {
        var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

using System.Net;
using AuditNode.API.IntegrationTests.Fixtures;
using FluentAssertions;

namespace AuditNode.API.IntegrationTests.Health;

[Trait("Category", "ApiIntegration")]
public sealed class HealthEndpointTests(AuditNodeWebApplicationFactory factory)
    : IClassFixture<AuditNodeWebApplicationFactory>
{
    [Fact]
    public async Task Live_and_ready_are_anonymous_and_healthy_when_database_is_migrated()
    {
        using var client = factory.CreateHttpsClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_is_unavailable_when_database_cannot_be_reached()
    {
        await using var unavailable = new AuditNodeWebApplicationFactory(
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=none;Password=none;Timeout=1;Command Timeout=1");
        using var client = unavailable.CreateHttpsClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}

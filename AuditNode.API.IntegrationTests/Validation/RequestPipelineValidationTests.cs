using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AuditNode.API.IntegrationTests.Fixtures;
using FluentAssertions;

namespace AuditNode.API.IntegrationTests.Validation;

[Trait("Category", "ApiIntegration")]
public sealed class RequestPipelineValidationTests(AuditNodeWebApplicationFactory factory)
    : IClassFixture<AuditNodeWebApplicationFactory>
{
    [Fact]
    public async Task Malformed_json_returns_problem_details_with_correlation_id()
    {
        using var client = AuthorizedClient();
        using var content = new StringContent("{not-json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/servers", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.Contains("X-Correlation-ID").Should().BeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().Should().Be(400);
    }

    [Fact]
    public async Task Invalid_server_model_returns_bad_request_before_application_service()
    {
        using var client = AuthorizedClient();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/servers", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Multipart_import_without_file_returns_bad_request()
    {
        using var client = AuthorizedClient();
        using var content = new MultipartFormDataContent();

        var response = await client.PostAsync("/api/v1/inventory/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private HttpClient AuthorizedClient()
    {
        var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.Tokens.CreateToken());
        return client;
    }
}

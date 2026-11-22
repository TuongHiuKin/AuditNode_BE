using System.Net;
using System.Net.Http.Headers;
using AuditNode.API.IntegrationTests.Fixtures;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AuditNode.API.IntegrationTests;

[Trait("Category", "ApiIntegration")]
public sealed class CatalogSearchPipelineTests(AuditNodeWebApplicationFactory factory)
    : IClassFixture<AuditNodeWebApplicationFactory>
{
    [Fact]
    public async Task Search_runs_through_PostgreSQL_and_the_HTTP_pipeline()
    {
        var ownerId = $"search-owner-{Guid.NewGuid():N}";
        var datacenterId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            database.Datacenters.Add(new Datacenter
            {
                Id = datacenterId,
                OwnerUserId = ownerId,
                Name = "Search DC",
                Location = "Test"
            });
            database.Servers.Add(new Server
            {
                Id = serverId,
                OwnerUserId = ownerId,
                DatacenterId = datacenterId,
                IpAddress = "192.0.2.10",
                Hostname = $"searchneedle-{serverId:N}",
                OsType = "Linux",
                Environment = "Test",
                Status = "Online"
            });
            await database.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IGlobalCatalogRepository>();
            var result = await repository.SearchAsync(
                ownerId,
                "searchneedle",
                new CatalogPageQuery(CatalogView.Mine, 25),
                DateTime.UtcNow);
            result.Items.Should().ContainSingle(item => item.Id == serverId);
        }

        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.Tokens.CreateToken(subject: ownerId));

        var response = await client.GetAsync("/api/v1/search?q=searchneedle&view=mine&limit=25");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(serverId.ToString());
    }
}

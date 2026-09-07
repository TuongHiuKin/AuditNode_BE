using System.Net;
using System.Net.Http.Headers;
using AuditNode.API.IntegrationTests.Fixtures;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AuditNode.API.IntegrationTests.Authorization;

[Trait("Category", "ApiIntegration")]
public sealed class OwnerIsolationTests(AuditNodeWebApplicationFactory factory)
    : IClassFixture<AuditNodeWebApplicationFactory>
{
    [Fact]
    public async Task Cross_owner_detail_delete_export_and_topology_do_not_disclose_or_mutate_resource()
    {
        var ownerId = $"owner-{Guid.NewGuid():N}";
        var attackerId = $"attacker-{Guid.NewGuid():N}";
        var serverId = Guid.NewGuid();
        var datacenterId = Guid.NewGuid();
        var topologyNodeId = Guid.NewGuid();
        await SeedOwnerResource(ownerId, datacenterId, serverId, topologyNodeId);
        using var client = Authorized(attackerId);

        var detail = await client.GetAsync($"/api/v1/servers/{serverId}");
        var delete = await client.DeleteAsync($"/api/v1/servers/{serverId}");
        var export = await client.GetAsync($"/api/v1/servers/export?ids={serverId}&view=shared");
        var topology = await client.GetAsync($"/api/v1/topology/state?ownerUserId={ownerId}");

        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        (await export.Content.ReadAsStringAsync()).Should().Be("[]");
        topology.StatusCode.Should().Be(HttpStatusCode.OK);
        (await topology.Content.ReadAsStringAsync()).Should().NotContain(topologyNodeId.ToString(),
            "an arbitrary ownerUserId query must not bypass grant filtering");

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        (await database.Servers.FindAsync(serverId)).Should().NotBeNull();
    }

    private async Task SeedOwnerResource(string ownerId, Guid datacenterId, Guid serverId, Guid topologyNodeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        database.Datacenters.Add(new Datacenter
        {
            Id = datacenterId,
            OwnerUserId = ownerId,
            Name = "Isolated DC",
            Location = "Test"
        });
        database.Servers.Add(new Server
        {
            Id = serverId,
            OwnerUserId = ownerId,
            DatacenterId = datacenterId,
            IpAddress = $"10.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}",
            Hostname = $"isolated-{serverId:N}",
            OsType = "Linux",
            Environment = "Test",
            Status = "Active"
        });
        database.TopologyNodes.Add(new TopologyNode
        {
            Id = topologyNodeId,
            OwnerUserId = ownerId,
            NodeType = "server",
            Label = "Restricted node",
            ReferenceId = serverId
        });
        await database.SaveChangesAsync();
    }

    private HttpClient Authorized(string userId)
    {
        var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.Tokens.CreateToken(subject: userId));
        return client;
    }
}

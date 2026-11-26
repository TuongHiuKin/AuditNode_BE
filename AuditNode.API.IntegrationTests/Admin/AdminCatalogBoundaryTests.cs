using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AuditNode.API.IntegrationTests.Fixtures;
using AuditNode.Application.DTOs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuditNode.API.IntegrationTests.Admin;

/// <summary>
/// The administrator boundary: a SystemAdmin may read another user's catalog and manage
/// their sharing, but must not be able to change a single catalog row.
/// </summary>
[Trait("Category", "ApiIntegration")]
public sealed class AdminCatalogBoundaryTests(AuditNodeWebApplicationFactory factory)
    : IClassFixture<AuditNodeWebApplicationFactory>
{
    [Fact]
    public async Task A_system_admin_reads_another_users_catalog()
    {
        var ownerId = await SeedOwnerCatalogAsync("boundary-read");
        using var client = AdminClient();

        var response = await client.GetAsync($"/api/v1/admin/users/{ownerId}/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var catalog = await response.Content.ReadFromJsonAsync<AdminOwnerCatalogDto>();
        catalog!.OwnerUserId.Should().Be(ownerId);
        catalog.Servers.Should().ContainSingle().Which.Hostname.Should().Be("boundary-host");
        catalog.Applications.Should().ContainSingle().Which.AppCode.Should().Be("BOUNDARY-APP");
        catalog.ExternalServices.Should().ContainSingle().Which.Name.Should().Be("Boundary Stripe");
        catalog.Labels.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_normal_user_cannot_read_another_users_catalog()
    {
        var ownerId = await SeedOwnerCatalogAsync("boundary-denied");
        using var client = Client(factory.Tokens.CreateToken(subject: "ordinary-user"));

        var response = await client.GetAsync($"/api/v1/admin/users/{ownerId}/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task There_is_no_write_endpoint_under_the_admin_catalog_route()
    {
        var ownerId = await SeedOwnerCatalogAsync("boundary-write");
        using var client = AdminClient();

        // The route is read-only by design: every write verb must be rejected or unrouted.
        var post = await client.PostAsJsonAsync($"/api/v1/admin/users/{ownerId}/catalog", new { hostname = "hacked" });
        var put = await client.PutAsJsonAsync($"/api/v1/admin/users/{ownerId}/catalog", new { hostname = "hacked" });
        var delete = await client.DeleteAsync($"/api/v1/admin/users/{ownerId}/catalog");

        using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.AuditDbContext>();
        var hostnames = await database.Servers.IgnoreQueryFilters()
            .Where(server => server.OwnerUserId == ownerId)
            .Select(server => server.Hostname)
            .ToListAsync();

        hostnames.Should().BeEquivalentTo(["boundary-host"]);
        new[] { post.StatusCode, put.StatusCode, delete.StatusCode }
            .Should().NotContain(HttpStatusCode.OK)
            .And.NotContain(HttpStatusCode.NoContent)
            .And.NotContain(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_missing_user_returns_not_found()
    {
        using var client = AdminClient();

        var response = await client.GetAsync($"/api/v1/admin/users/{Guid.NewGuid():N}/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient AdminClient() =>
        Client(factory.Tokens.CreateToken(subject: "admin-user", systemAdmin: true));

    private HttpClient Client(string token)
    {
        var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Seeds a complete catalog for a real identity, because the API has no path that
    /// writes into somebody else's catalog by design.
    /// </summary>
    private async Task<string> SeedOwnerCatalogAsync(string prefix)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.AuditDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Infrastructure.Identity.AppUser>>();

        // The admin catalog endpoint resolves the owner through the identity store first.
        var ownerId = Guid.NewGuid().ToString();
        var username = $"admin-boundary-{prefix}";
        (await users.CreateAsync(
            new Infrastructure.Identity.AppUser { Id = ownerId, UserName = username, Email = $"{username}@example.test" },
            "Sup3rSecret!pass")).Succeeded.Should().BeTrue();

        var datacenter = new Domain.Entities.Datacenter
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerId, Name = $"{prefix}-dc", Location = "test",
        };
        var server = new Domain.Entities.Server
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerId, DatacenterId = datacenter.Id,
            Hostname = "boundary-host", IpAddress = "10.77.0.1", OsType = "Linux",
            Environment = "Test", Status = "Online", Datacenter = datacenter,
        };
        var application = new Domain.Entities.Application
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerId, AppCode = "BOUNDARY-APP",
            AppName = "Boundary app", OwnerTeam = "test", Risk = "Low", Icon = "app",
            TechStack = "test",
        };
        var port = new Domain.Entities.PortMapping
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerId, AppId = application.Id,
            ServerId = server.Id, PortNumber = 8080, Protocol = "TCP",
        };
        var external = new Domain.Entities.ExternalService
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerId, Name = "Boundary Stripe",
            Kind = "SaaS", Environment = "Test",
        };
        var label = new Domain.Entities.Label
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerId, Key = "scope", Value = prefix,
            // ck_labels_owner_protected requires an owner-kind label to be protected.
            Kind = Domain.Entities.LabelKinds.Owner, IsProtected = true,
        };

        database.AddRange(datacenter, server, application, port, external, label);
        await database.SaveChangesAsync();
        return ownerId;
    }
}

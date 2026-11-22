using AuditNode.Application.DTOs;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Repositories;

public sealed class ApplicationRepositoryTests
{
    private static AuditDbContext Context()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options);
    }

    private static (AuditDbContext Context, Server Server) Seed()
    {
        var context = Context();
        var datacenter = new Datacenter { Id = Guid.NewGuid(), Name = "Local", Location = "Site", OwnerUserId = "owner" };
        var server = new Server
        {
            Id = Guid.NewGuid(),
            DatacenterId = datacenter.Id,
            IpAddress = "10.0.0.1",
            Hostname = "srv1",
            OsType = "Linux",
            Environment = "Production",
            Status = "Active",
            OwnerUserId = "owner",
        };
        context.Datacenters.Add(datacenter);
        context.Servers.Add(server);
        context.SaveChanges();
        return (context, server);
    }

    private static AppEntity Application(string code = "APP01", string name = "Portal") => new()
    {
        Id = Guid.NewGuid(),
        AppCode = code,
        AppName = name,
        OwnerTeam = "Platform",
        OwnerUserId = "owner",
    };

    [Fact]
    public async Task AppCodeExistsAsync_detects_a_code_inside_the_owner_catalog()
    {
        var (context, _) = Seed();
        await using var _ = context;
        context.Applications.Add(Application());
        await context.SaveChangesAsync();
        var repository = new ApplicationRepository(context);

        (await repository.AppCodeExistsAsync("APP01", "owner")).Should().BeTrue();
    }

    [Fact]
    public async Task AppCodeExistsAsync_ignores_the_application_being_updated()
    {
        var (context, _) = Seed();
        await using var _ = context;
        var application = Application();
        context.Applications.Add(application);
        await context.SaveChangesAsync();
        var repository = new ApplicationRepository(context);

        (await repository.AppCodeExistsAsync("APP01", "owner", application.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task AppCodeExistsAsync_ignores_another_owner_with_the_same_code()
    {
        var (context, _) = Seed();
        await using var _ = context;
        context.Applications.Add(Application());
        await context.SaveChangesAsync();
        var repository = new ApplicationRepository(context);

        (await repository.AppCodeExistsAsync("APP01", "other")).Should().BeFalse();
    }

    [Fact]
    public async Task ServerExistsAsync_requires_the_server_to_belong_to_the_owner()
    {
        var (context, server) = Seed();
        await using var _ = context;
        var repository = new ApplicationRepository(context);

        (await repository.ServerExistsAsync(server.Id, "owner")).Should().BeTrue();
        (await repository.ServerExistsAsync(server.Id, "other")).Should().BeFalse();
    }

    [Fact]
    public async Task PortCollisionExistsAsync_detects_an_occupied_port_on_the_same_server()
    {
        var (context, server) = Seed();
        await using var _ = context;
        var application = Application();
        context.Applications.Add(application);
        context.PortMappings.Add(new PortMapping
        {
            Id = Guid.NewGuid(),
            ServerId = server.Id,
            AppId = application.Id,
            PortNumber = 443,
            Protocol = "HTTPS",
            OwnerUserId = "owner",
        });
        await context.SaveChangesAsync();
        var repository = new ApplicationRepository(context);

        (await repository.PortCollisionExistsAsync(server.Id, 443, "owner"))
            .Should().BeTrue();
        (await repository.PortCollisionExistsAsync(server.Id, 8443, "owner"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_persists_the_mapping_and_the_labels_atomically()
    {
        var (context, server) = Seed();
        await using var _ = context;
        var repository = new ApplicationRepository(context);
        var application = Application();
        var deployment = new PortMapping
        {
            Id = Guid.NewGuid(),
            ServerId = server.Id,
            AppId = application.Id,
            PortNumber = 443,
            Protocol = "HTTPS",
            OwnerUserId = "owner",
        };

        await repository.CreateAsync(application, [new LabelDto { Key = "env", Value = "prod" }], deployment);

        var created = await repository.GetByIdAsync(application.Id);
        created!.PortMappings.Should().ContainSingle().Which.Id.Should().Be(deployment.Id);
        created.ApplicationLabels.Should().ContainSingle().Which.Label!.Key.Should().Be("env");
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_label_set_and_keeps_the_deployment()
    {
        var (context, server) = Seed();
        await using var _ = context;
        var repository = new ApplicationRepository(context);
        var application = Application();
        var deployment = new PortMapping
        {
            Id = Guid.NewGuid(),
            ServerId = server.Id,
            AppId = application.Id,
            PortNumber = 443,
            Protocol = "HTTPS",
            OwnerUserId = "owner",
        };
        await repository.CreateAsync(application, [new LabelDto { Key = "env", Value = "prod" }], deployment);

        await repository.UpdateAsync(application, [new LabelDto { Key = "tier", Value = "web" }], deployment);

        var updated = await repository.GetByIdAsync(application.Id);
        updated!.ApplicationLabels.Should().ContainSingle().Which.Label!.Key.Should().Be("tier");
        updated.PortMappings.Should().ContainSingle().Which.Id.Should().Be(deployment.Id);
    }

    [Fact]
    public async Task GetByIdAsync_returns_the_deployments_with_their_server()
    {
        var (context, server) = Seed();
        await using var _ = context;
        var repository = new ApplicationRepository(context);
        var application = Application();
        await repository.CreateAsync(application, [], new PortMapping
        {
            Id = Guid.NewGuid(),
            ServerId = server.Id,
            AppId = application.Id,
            PortNumber = 443,
            Protocol = "HTTPS",
            OwnerUserId = "owner",
        });

        var loaded = await repository.GetByIdAsync(application.Id);

        loaded!.PortMappings.Should().ContainSingle().Which.Server!.Hostname.Should().Be("srv1");
    }
}

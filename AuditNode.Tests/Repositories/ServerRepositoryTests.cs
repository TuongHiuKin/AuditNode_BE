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

public sealed class ServerRepositoryTests
{
    private static AuditDbContext Context()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options);
    }

    private static (AuditDbContext Context, Datacenter Datacenter, Server Server) Seed(string owner = "owner")
    {
        var context = Context();
        var datacenter = new Datacenter { Id = Guid.NewGuid(), Name = "Local", Location = "Site", OwnerUserId = owner };
        var server = new Server
        {
            Id = Guid.NewGuid(),
            DatacenterId = datacenter.Id,
            IpAddress = "10.0.0.1",
            Hostname = "srv1",
            OsType = "Linux",
            Environment = "Production",
            Status = "Active",
            OwnerUserId = owner,
        };
        context.Datacenters.Add(datacenter);
        context.Servers.Add(server);
        context.SaveChanges();
        return (context, datacenter, server);
    }

    [Fact]
    public async Task DatacenterExistsAsync_accepts_the_owner_datacenter()
    {
        var (context, datacenter, _) = Seed();
        await using var _ = context;
        var repository = new ServerRepository(context);

        (await repository.DatacenterExistsAsync(datacenter.Id, "owner")).Should().BeTrue();
    }

    [Fact]
    public async Task DatacenterExistsAsync_rejects_a_datacenter_owned_by_another_user()
    {
        var (context, datacenter, _) = Seed();
        await using var _ = context;
        var repository = new ServerRepository(context);

        (await repository.DatacenterExistsAsync(datacenter.Id, "other")).Should().BeFalse();
    }

    [Fact]
    public async Task IpAddressExistsAsync_detects_a_collision_inside_the_owner_catalog()
    {
        var (context, _, server) = Seed();
        await using var _ = context;
        var repository = new ServerRepository(context);

        (await repository.IpAddressExistsAsync(server.IpAddress, "owner")).Should().BeTrue();
    }

    [Fact]
    public async Task IpAddressExistsAsync_ignores_the_server_being_updated()
    {
        var (context, _, server) = Seed();
        await using var _ = context;
        var repository = new ServerRepository(context);

        (await repository.IpAddressExistsAsync(server.IpAddress, "owner", server.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task IpAddressExistsAsync_ignores_another_owner_with_the_same_address()
    {
        var (context, _, server) = Seed();
        await using var _ = context;
        var repository = new ServerRepository(context);

        (await repository.IpAddressExistsAsync(server.IpAddress, "other")).Should().BeFalse();
    }

    [Fact]
    public async Task GetAllWithAppsAsync_returns_the_datacenter_name_and_deployments()
    {
        var (context, datacenter, server) = Seed();
        await using var _ = context;
        var application = new AppEntity
        {
            Id = Guid.NewGuid(),
            AppCode = "APP01",
            AppName = "Portal",
            OwnerTeam = "Platform",
            OwnerUserId = "owner",
        };
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
        context.SaveChanges();
        var repository = new ServerRepository(context);

        var result = (await repository.GetAllWithAppsAsync()).ToList();

        result.Should().ContainSingle();
        result[0].Datacenter.Should().Be(datacenter.Name);
        result[0].Applications.Should().ContainSingle().Which.PortNumber.Should().Be(443);
    }

    [Fact]
    public async Task GetAllWithAppsAsync_filters_by_environment()
    {
        var (context, _, _) = Seed();
        await using var _ = context;
        var repository = new ServerRepository(context);

        (await repository.GetAllWithAppsAsync(environment: "Staging")).Should().BeEmpty();
        (await repository.GetAllWithAppsAsync(environment: "Production")).Should().ContainSingle();
    }

    [Fact]
    public async Task CreateServerAsync_persists_the_labels_of_the_new_server()
    {
        var (context, datacenter, _) = Seed();
        await using var _ = context;
        var repository = new ServerRepository(context);
        var server = new Server
        {
            Id = Guid.NewGuid(),
            DatacenterId = datacenter.Id,
            IpAddress = "10.0.0.9",
            Hostname = "srv9",
            OsType = "Linux",
            Environment = "Production",
            Status = "Active",
            OwnerUserId = "owner",
        };

        await repository.CreateServerAsync(server, [new LabelDto { Key = "env", Value = "prod" }]);

        var created = await repository.GetByIdAsync(server.Id);
        created!.Labels.Should().ContainSingle().Which.Key.Should().Be("env");
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_label_set_instead_of_appending()
    {
        var (context, datacenter, _) = Seed();
        await using var _ = context;
        var repository = new ServerRepository(context);
        var server = new Server
        {
            Id = Guid.NewGuid(),
            DatacenterId = datacenter.Id,
            IpAddress = "10.0.0.7",
            Hostname = "srv7",
            OsType = "Linux",
            Environment = "Production",
            Status = "Active",
            OwnerUserId = "owner",
        };
        await repository.CreateServerAsync(server, [new LabelDto { Key = "env", Value = "prod" }]);

        await repository.UpdateAsync(server, [new LabelDto { Key = "tier", Value = "web" }]);

        var updated = await repository.GetByIdAsync(server.Id);
        updated!.Labels.Should().ContainSingle().Which.Key.Should().Be("tier");
    }

    [Fact]
    public async Task DeleteAsync_removes_the_server()
    {
        var (context, _, server) = Seed();
        await using var _ = context;
        var repository = new ServerRepository(context);

        await repository.DeleteAsync(server);

        (await repository.GetByIdAsync(server.Id)).Should().BeNull();
    }
}

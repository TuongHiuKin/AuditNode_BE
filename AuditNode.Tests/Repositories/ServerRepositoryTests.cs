using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace AuditNode.Tests.Repositories;

public class ServerRepositoryTests
{
    private AuditDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.WorkspaceId).Returns(Guid.NewGuid());
        return new AuditDbContext(options, mockTenantProvider.Object);
    }

    [Fact]
    public async Task Datacenter_and_ip_checks_do_not_see_another_workspace()
    {
        var databaseName = Guid.NewGuid().ToString();
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var tenantA = new Mock<ITenantProvider>();
        tenantA.SetupGet(x => x.WorkspaceId).Returns(workspaceA);
        var tenantB = new Mock<ITenantProvider>();
        tenantB.SetupGet(x => x.WorkspaceId).Returns(workspaceB);

        var datacenterA = new Datacenter { Id = Guid.NewGuid(), Name = "A", Location = "A" };
        await using (var contextA = new AuditDbContext(options, tenantA.Object))
        {
            contextA.Datacenters.Add(datacenterA);
            contextA.Servers.Add(new Server
            {
                Id = Guid.NewGuid(), DatacenterId = datacenterA.Id, IpAddress = "10.0.0.1",
                Hostname = "A", OsType = "Linux", Environment = "Prod", Status = "Active"
            });
            await contextA.SaveChangesAsync();
        }

        var datacenterB = new Datacenter { Id = Guid.NewGuid(), Name = "B", Location = "B" };
        await using (var contextB = new AuditDbContext(options, tenantB.Object))
        {
            contextB.Datacenters.Add(datacenterB);
            contextB.Servers.Add(new Server
            {
                Id = Guid.NewGuid(), DatacenterId = datacenterB.Id, IpAddress = "10.0.0.2",
                Hostname = "B", OsType = "Linux", Environment = "Prod", Status = "Active"
            });
            await contextB.SaveChangesAsync();
        }

        await using var readContext = new AuditDbContext(options, tenantA.Object);
        var repository = new ServerRepository(readContext);

        (await repository.DatacenterExistsAsync(datacenterA.Id)).Should().BeTrue();
        (await repository.DatacenterExistsAsync(datacenterB.Id)).Should().BeFalse();
        (await repository.IpAddressExistsAsync("10.0.0.1")).Should().BeTrue();
        (await repository.IpAddressExistsAsync("10.0.0.2")).Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnServer_WithDatacenter()
    {
        // Arrange
        using var context = GetDbContext();
        var datacenter = new Datacenter { Id = Guid.NewGuid(), Name = "DC1", Location = "Location 1" };
        var server = new Server { Id = Guid.NewGuid(), Hostname = "SRV-01", IpAddress = "192.168.1.1", DatacenterId = datacenter.Id, Status = "UP", OsType = "Linux", Environment = "Prod" };
        
        context.Datacenters.Add(datacenter);
        context.Servers.Add(server);
        await context.SaveChangesAsync();

        var repository = new ServerRepository(context);

        // Act
        var result = await repository.GetByIdAsync(server.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Hostname.Should().Be("SRV-01");
        result.Datacenter.Should().NotBeNull();
        result.Datacenter!.Name.Should().Be("DC1");
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateServerFields()
    {
        // Arrange
        using var context = GetDbContext();
        var dc = new Datacenter { Id = Guid.NewGuid(), Name = "DC", Location = "Loc" };
        var server = new Server { Id = Guid.NewGuid(), Hostname = "OldName", IpAddress = "10.0.0.1", DatacenterId = dc.Id, Status = "UP", OsType = "Linux", Environment = "Prod" };
        context.Datacenters.Add(dc);
        context.Servers.Add(server);
        await context.SaveChangesAsync();

        var repository = new ServerRepository(context);
        server.Hostname = "NewName";

        // Act
        await repository.UpdateAsync(server, null);

        // Assert
        var updatedServer = await context.Servers.FindAsync(server.Id);
        updatedServer.Should().NotBeNull();
        updatedServer!.Hostname.Should().Be("NewName");
    }

    [Fact]
    public async Task GetByIdsAsync_ShouldReturnOnlyRequestedServers()
    {
        // Arrange
        using var context = GetDbContext();
        var dc = new Datacenter { Id = Guid.NewGuid(), Name = "DC", Location = "Loc" };
        context.Datacenters.Add(dc);

        var s1 = new Server { Id = Guid.NewGuid(), Hostname = "S1", IpAddress = "1.1.1.1", DatacenterId = dc.Id, Status = "UP", OsType = "L", Environment = "P" };
        var s2 = new Server { Id = Guid.NewGuid(), Hostname = "S2", IpAddress = "2.2.2.2", DatacenterId = dc.Id, Status = "UP", OsType = "L", Environment = "P" };
        var s3 = new Server { Id = Guid.NewGuid(), Hostname = "S3", IpAddress = "3.3.3.3", DatacenterId = dc.Id, Status = "UP", OsType = "L", Environment = "P" };
        context.Servers.AddRange(s1, s2, s3);
        
        var app = new AuditNode.Domain.Entities.Application { Id = Guid.NewGuid(), AppCode = "A1", AppName = "App 1" };
        context.Applications.Add(app);
        context.PortMappings.Add(new PortMapping { Id = Guid.NewGuid(), AppId = app.Id, ServerId = s1.Id, PortNumber = 80, Protocol = "TCP" });
        context.PortMappings.Add(new PortMapping { Id = Guid.NewGuid(), AppId = app.Id, ServerId = s3.Id, PortNumber = 80, Protocol = "TCP" });

        await context.SaveChangesAsync();

        var repository = new ServerRepository(context);

        // Act
        var result = await repository.GetByIdsAsync(new[] { s1.Id, s3.Id });

        // Assert
        result.Should().HaveCount(2);
        result.Select(r => r.Id).Should().Contain(new[] { s1.Id, s3.Id });
        result.Select(r => r.Id).Should().NotContain(s2.Id);
        result.Single(r => r.Id == s1.Id).Applications.Single().PortMappingId.Should()
            .Be(context.PortMappings.Single(mapping => mapping.ServerId == s1.Id).Id);
    }

    [Fact]
    public async Task Create_and_UpdateServer_with_Labels_in_Default_Workspace_succeeds()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var defaultTenant = new Mock<ITenantProvider>();
        defaultTenant.SetupGet(x => x.WorkspaceId).Returns(Guid.Empty);

        var dc = new Datacenter { Id = Guid.NewGuid(), Name = "DC-Default", Location = "Loc" };
        var serverId = Guid.NewGuid();
        var server = new Server
        {
            Id = serverId,
            Hostname = "Server-Default",
            IpAddress = "192.168.1.50",
            DatacenterId = dc.Id,
            Status = "Active",
            OsType = "Linux",
            Environment = "Production"
        };

        var initialLabels = new List<AuditNode.Application.DTOs.LabelDto>
        {
            new() { Key = "env", Value = "prod" },
            new() { Key = "tier", Value = "backend" }
        };

        await using (var context = new AuditDbContext(options, defaultTenant.Object))
        {
            context.Datacenters.Add(dc);
            await context.SaveChangesAsync();

            var repository = new ServerRepository(context);
            await repository.CreateServerAsync(server, initialLabels);
        }

        await using (var context = new AuditDbContext(options, defaultTenant.Object))
        {
            var repository = new ServerRepository(context);
            var loaded = await repository.GetByIdAsync(serverId);
            loaded.Should().NotBeNull();
            loaded!.Labels.Should().HaveCount(2);

            var updatedLabels = new List<AuditNode.Application.DTOs.LabelDto>
            {
                new() { Key = "env", Value = "staging" },
                new() { Key = "tier", Value = "backend" },
                new() { Key = "team", Value = "core" }
            };

            await repository.UpdateAsync(loaded, updatedLabels);
        }

        await using (var context = new AuditDbContext(options, defaultTenant.Object))
        {
            var repository = new ServerRepository(context);
            var loaded = await repository.GetByIdAsync(serverId);
            loaded.Should().NotBeNull();
            loaded!.Labels.Should().HaveCount(3);
            loaded.Labels.Select(l => l.Key).Should().Contain(new[] { "env", "tier", "team" });
        }
    }
}

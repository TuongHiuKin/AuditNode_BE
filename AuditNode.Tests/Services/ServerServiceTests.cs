using AuditNode.Application.DTOs;
using AuditNode.Infrastructure.Services;
using AuditNode.Infrastructure.Data;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AuditNode.Tests.Services;

public class ServerServiceTests
{
    private AuditDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;        
        return new AuditDbContext(options);
    }

    [Fact]
    public async Task GetServersAsync_ShouldReturnServers()
    {
        using var context = GetDbContext();
        var serverId = Guid.NewGuid();
        var dcId = Guid.NewGuid();
        context.Datacenters.Add(new Datacenter { Id = dcId, Name = "DC1" });
        var app = new AuditNode.Domain.Entities.Application { Id = Guid.NewGuid(), AppCode = "A1", AppName = "App 1", OwnerTeam = "Team A"};
        context.Applications.Add(app);
        context.Servers.Add(new Server
        {
            Id = serverId,
            Hostname = "SRV-TEST",
            IpAddress = "10.0.0.1",
            DatacenterId = dcId,
            PortMappings = new List<PortMapping>
            {
                new PortMapping { Id = Guid.NewGuid(), AppId = app.Id, PortNumber = 80, Protocol = "TCP", Application = app }
            }
        });
        await context.SaveChangesAsync();

        var service = new ServerService(context);

        var result = await service.GetServersAsync();

        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result.First().Hostname.Should().Be("SRV-TEST");
    }

    [Fact]
    public async Task GetServersAsync_WithLabels_ShouldFilterCorrectly()
    {
        using var context = GetDbContext();
        var dcId = Guid.NewGuid();
        context.Datacenters.Add(new Datacenter { Id = dcId, Name = "DC1" });
        
        var labelProd = new Label { Id = Guid.NewGuid(), Key = "env", Value = "production" };
        var labelDev = new Label { Id = Guid.NewGuid(), Key = "env", Value = "dev" };
        
        var srvProd = new Server { Id = Guid.NewGuid(), Hostname = "SRV-PROD", DatacenterId = dcId, Labels = new List<Label> { labelProd } };
        var srvDev = new Server { Id = Guid.NewGuid(), Hostname = "SRV-DEV", DatacenterId = dcId, Labels = new List<Label> { labelDev } };
        var srvNone = new Server { Id = Guid.NewGuid(), Hostname = "SRV-NONE", DatacenterId = dcId };

        context.Servers.AddRange(srvProd, srvDev, srvNone);
        await context.SaveChangesAsync();

        var service = new ServerService(context);

        var resultProd = await service.GetServersAsync(new[] { "production" });
        resultProd.Should().HaveCount(1);
        resultProd.First().Hostname.Should().Be("SRV-PROD");

        var resultBoth = await service.GetServersAsync(new[] { "env" });
        resultBoth.Should().HaveCount(2);

        var resultNone = await service.GetServersAsync(new[] { "staging" });
        resultNone.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportServersAsync_ShouldReturnSelectedServers()
    {
        using var context = GetDbContext();
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var dcId = Guid.NewGuid();
        context.Datacenters.Add(new Datacenter { Id = dcId, Name = "DC1" });
        var app = new AuditNode.Domain.Entities.Application { Id = Guid.NewGuid(), AppCode = "A1", AppName = "App 1", OwnerTeam = "Team A"};
        context.Applications.Add(app);
        var pm = new PortMapping { Id = Guid.NewGuid(), AppId = app.Id, PortNumber = 80, Protocol = "TCP", Application = app };
        context.Servers.Add(new Server { Id = ids[0], Hostname = "S1", DatacenterId = dcId, PortMappings = new List<PortMapping> { pm } });
        context.Servers.Add(new Server { Id = ids[1], Hostname = "S2", DatacenterId = dcId, PortMappings = new List<PortMapping>() });
        context.Servers.Add(new Server { Id = Guid.NewGuid(), Hostname = "S3", DatacenterId = dcId, PortMappings = new List<PortMapping>() });
        await context.SaveChangesAsync();

        var service = new ServerService(context);

        var result = await service.ExportServersAsync(ids);

        result.Should().HaveCount(2);
        result.Select(s => s.Hostname).Should().Contain(new[] { "S1", "S2" });
        result.Select(s => s.Hostname).Should().NotContain("S3");
    }

    [Fact]
    public async Task UpdateServerAsync_ShouldUpdateAndReturnTrue_WhenServerExists()
    {
        using var context = GetDbContext();
        var serverId = Guid.NewGuid();
        context.Servers.Add(new Server { Id = serverId, Hostname = "OldHost", DatacenterId = Guid.NewGuid()});
        await context.SaveChangesAsync();

        var service = new ServerService(context);
        var updateDto = new UpdateServerDto { Hostname = "NewHost", OsType = "Linux" };

        var result = await service.UpdateServerAsync(serverId, updateDto);

        result.Should().BeTrue();
        var updatedServer = await context.Servers.FindAsync(serverId);
        updatedServer!.Hostname.Should().Be("NewHost");
        updatedServer.OsType.Should().Be("Linux");
    }

    [Fact]
    public async Task UpdateServerAsync_ShouldReturnFalse_WhenServerDoesNotExist()
    {
        using var context = GetDbContext();
        var service = new ServerService(context);
        var updateDto = new UpdateServerDto { Hostname = "NewHost" };

        var result = await service.UpdateServerAsync(Guid.NewGuid(), updateDto);

        result.Should().BeFalse();
    }
    [Fact]
    public async Task CreateServerAsync_ShouldAddNewServer_AndReturnDto()
    {
        using var context = GetDbContext();
        var service = new ServerService(context);
        var dcId = Guid.NewGuid();
        context.Datacenters.Add(new Datacenter { Id = dcId, Name = "DC" });
        await context.SaveChangesAsync();
        var dto = new CreateServerDto { Hostname = "NEW-SRV", IpAddress = "192.168.1.1", OsType = "Windows", DatacenterId = dcId };

        var result = await service.CreateServerAsync(dto);

        result.Should().NotBeNull();
        result.Hostname.Should().Be("NEW-SRV");
        
        var inDb = await context.Servers.FirstOrDefaultAsync(s => s.Hostname == "NEW-SRV");
        inDb.Should().NotBeNull();
    }
}



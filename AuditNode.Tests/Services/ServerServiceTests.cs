using AuditNode.Application.DTOs;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using AuditNode.Application.Interfaces;

namespace AuditNode.Tests.Services;

public class ServerServiceTests
{
    private AuditDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
            
        var tenantProviderMock = new Mock<ITenantProvider>();
        tenantProviderMock.Setup(t => t.WorkspaceId).Returns(Guid.Empty);
        return new AuditDbContext(options, tenantProviderMock.Object);
    }

    [Fact]
    public async Task GetServersAsync_ShouldReturnAllServers()
    {
        using var context = CreateDbContext();
        var service = new ServerService(context);
        
        var dc = new Datacenter { Id = Guid.NewGuid(), Name = "DC", Location = "Loc" };
        context.Datacenters.Add(dc);
        context.Servers.AddRange(
            new Server { Id = Guid.NewGuid(), Hostname = "SRV-1", IpAddress = "10.0.0.1", DatacenterId = dc.Id },
            new Server { Id = Guid.NewGuid(), Hostname = "SRV-2", IpAddress = "10.0.0.2", DatacenterId = dc.Id }
        );
        await context.SaveChangesAsync();

        var result = await service.GetServersAsync();

        result.Should().NotBeNull();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExportServersAsync_ShouldReturnSelectedServers()
    {
        using var context = CreateDbContext();
        var service = new ServerService(context);
        
        var dc = new Datacenter { Id = Guid.NewGuid(), Name = "DC", Location = "Loc" };
        context.Datacenters.Add(dc);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        context.Servers.AddRange(
            new Server { Id = id1, Hostname = "SRV-1", IpAddress = "10.0.0.1", DatacenterId = dc.Id },
            new Server { Id = id2, Hostname = "SRV-2", IpAddress = "10.0.0.2", DatacenterId = dc.Id },
            new Server { Id = id3, Hostname = "SRV-3", IpAddress = "10.0.0.3", DatacenterId = dc.Id }
        );
        await context.SaveChangesAsync();

        var result = await service.ExportServersAsync(new List<Guid> { id1, id3 });

        result.Should().NotBeNull();
        result.Should().HaveCount(2);
    }
}

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

public class DatacenterRepositoryTests
{
    private AuditDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.WorkspaceId).Returns(Guid.Empty);
        return new AuditDbContext(options, mockTenantProvider.Object);
    }

    [Fact]
    public async Task CreateDatacenterAsync_ShouldAddDatacenter()
    {
        // Arrange
        using var context = GetDbContext();
        var repository = new DatacenterRepository(context);
        var dc = new Datacenter { Id = Guid.NewGuid(), Name = "DC1", Location = "Loc1" };

        // Act
        var result = await repository.CreateDatacenterAsync(dc);

        // Assert
        result.Should().NotBeNull();
        context.Datacenters.Should().Contain(d => d.Name == "DC1");
    }

    [Fact]
    public async Task GetAllDatacentersAsync_ShouldReturnAll()
    {
        // Arrange
        using var context = GetDbContext();
        context.Datacenters.AddRange(new List<Datacenter>
        {
            new Datacenter { Id = Guid.NewGuid(), Name = "DC1", Location = "Loc1" },
            new Datacenter { Id = Guid.NewGuid(), Name = "DC2", Location = "Loc2" }
        });
        await context.SaveChangesAsync();
        var repository = new DatacenterRepository(context);

        // Act
        var result = await repository.GetAllDatacentersAsync();

        // Assert
        result.Should().HaveCount(2);
    }
}

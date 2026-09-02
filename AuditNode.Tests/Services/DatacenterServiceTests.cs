using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class DatacenterServiceTests
{
    [Fact]
    public async Task Create_ensures_owner_label_before_persisting_owner_resource()
    {
        await using var context = CreateContext();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns("owner-user-id");
        var ownerLabels = new Mock<IOwnerLabelService>();
        var service = new DatacenterService(
            context,
            currentUser.Object,
            Mock.Of<IGlobalCatalogRepository>(),
            ownerLabels.Object,
            TimeProvider.System);

        var result = await service.CreateDatacenterAsync(new CreateDatacenterDto
        {
            Name = "Primary",
            Location = "Saigon"
        });

        result.OwnerUserId.Should().Be("owner-user-id");
        ownerLabels.Verify(item => item.EnsureAsync("owner-user-id", It.IsAny<CancellationToken>()), Times.Once);
        (await context.Datacenters.SingleAsync()).OwnerUserId.Should().Be("owner-user-id");
    }

    private static AuditDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AuditDbContext(options);
    }
}

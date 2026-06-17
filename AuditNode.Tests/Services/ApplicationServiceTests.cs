using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Services;
using AuditNode.Infrastructure.Data;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Services;

public class ApplicationServiceTests
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
    public async Task GetAllAsync_ShouldReturnApplications()
    {
        // Arrange
        using var context = GetDbContext();
        var appId = Guid.NewGuid();
        context.Applications.Add(new AppEntity
        {
            Id = appId,
            AppCode = "APP1",
            AppName = "Test App",
            OwnerTeam = "Team A"
        });
        await context.SaveChangesAsync();

        var service = new ApplicationService(context);

        // Act
        var result = await service.GetAllAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result.First().AppCode.Should().Be("APP1");
    }

    [Fact]
    public async Task CreateAsync_ShouldAddNewApplication()
    {
        // Arrange
        using var context = GetDbContext();
        var service = new ApplicationService(context);
        var dto = new CreateApplicationDto { AppCode = "NEW", AppName = "New App", OwnerTeam = "Team B" };

        // Act
        var result = await service.CreateAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.AppCode.Should().Be("NEW");
        context.Applications.Should().Contain(a => a.AppCode == "NEW");
    }
}

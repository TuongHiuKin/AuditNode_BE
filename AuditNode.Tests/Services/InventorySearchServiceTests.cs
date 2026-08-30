using AuditNode.Infrastructure.Services;
using AuditNode.Infrastructure.Data;
using AuditNode.Domain.Entities;
using AuditNode.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Moq;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Services;

public class InventorySearchServiceTests
{
    private readonly AuditDbContext _context;
    private readonly InventorySearchService _service;

    public InventorySearchServiceTests()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.WorkspaceId).Returns(Guid.NewGuid());
        _context = new AuditDbContext(options, mockTenantProvider.Object);
        var policy = new Mock<IScopedResourcePolicy>();
        policy.Setup(x => x.GetReadableIdsAsync(It.IsAny<Guid>(), "test-user", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlySet<Guid>?)null);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.UserId).Returns("test-user");
        _service = new InventorySearchService(_context, policy.Object, currentUser.Object, mockTenantProvider.Object, Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    public async Task SearchAsync_ShouldReturnEmpty_WhenKeywordIsShortOrNull(string? keyword)
    {
        // Act
        var result = await _service.SearchAsync(keyword!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnServer_WhenHostnameMatches()
    {
        // Arrange
        var server = new Server { Id = Guid.NewGuid(), Hostname = "ProductionServer01", IpAddress = "10.0.0.1", Environment = "Prod", Status = "Active", DatacenterId = Guid.NewGuid() };
        _context.Servers.Add(server);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SearchAsync("Production");

        // Assert
        result.Should().HaveCount(1);
        result.First().Type.Should().Be("SERVER");
        result.First().Title.Should().Be("ProductionServer01");
        result.First().MatchReason.Should().Be("Matched by Server Hostname");
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnApp_WhenAppNameMatches()
    {
        // Arrange
        var server = new Server { Id = Guid.NewGuid(), Hostname = "Host01", IpAddress = "10.0.0.2", Environment = "Prod", Status = "Active", DatacenterId = Guid.NewGuid() };
        var app = new AppEntity { Id = Guid.NewGuid(), AppName = "CustomerPortal", AppCode = "CP01" };
        var pm = new PortMapping { Id = Guid.NewGuid(), AppId = app.Id, ServerId = server.Id, PortNumber = 443, Application = app, Server = server };
        
        _context.Servers.Add(server);
        _context.Applications.Add(app);
        _context.PortMappings.Add(pm);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SearchAsync("Portal");

        // Assert
        result.Should().HaveCount(1);
        result.First().Type.Should().Be("APP");
        result.First().Title.Should().Be("CustomerPortal");
        result.First().Subtitle.Should().Be("On Server: Host01 (Port: 443)");
        result.First().MatchReason.Should().Be("Matched by App Name");
    }

    [Fact]
    public async Task SearchAsync_ShouldLimitResultsTo20()
    {
        // Arrange
        for (int i = 0; i < 25; i++)
        {
            _context.Servers.Add(new Server 
            { 
                Id = Guid.NewGuid(), 
                Hostname = $"Server{i:D2}", 
                IpAddress = $"10.1.0.{i}", 
                Environment = "Test", 
                Status = "Active", 
                DatacenterId = Guid.NewGuid() 
            });
        }
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SearchAsync("Server");

        // Assert
        result.Should().HaveCount(20);
    }
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class InventorySearchServiceTests
{
    private static InventorySearchService Service(
        string? userId = "owner",
        Action<Mock<IGlobalCatalogRepository>>? configure = null)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(userId);

        var catalog = new Mock<IGlobalCatalogRepository>();
        configure?.Invoke(catalog);

        return new InventorySearchService(currentUser.Object, catalog.Object, TimeProvider.System);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_returns_empty_when_the_actor_is_not_authenticated(string? userId)
    {
        var service = Service(userId);

        var result = await service.SearchAsync("Server");

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_returns_empty_for_a_keyword_shorter_than_two_characters(string keyword)
    {
        var service = Service();

        var result = await service.SearchAsync(keyword);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_delegates_to_the_owner_catalog_for_a_qualified_keyword()
    {
        var expected = new SearchResultDto
        {
            Type = "SERVER",
            Title = "ProductionServer01",
            MatchReason = "Matched by Server Hostname",
        };
        CatalogPageQuery? capturedQuery = null;
        string? capturedKeyword = null;
        string? capturedUserId = null;

        var service = Service(configure: catalog => catalog
            .Setup(repository => repository.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CatalogPageQuery>(),
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, CatalogPageQuery, DateTime, string?, string?, string?, CancellationToken>(
                (userId, keyword, query, _, _, _, _, _) =>
                {
                    capturedUserId = userId;
                    capturedKeyword = keyword;
                    capturedQuery = query;
                })
            .ReturnsAsync(new CursorPageDto<SearchResultDto>([expected], null, false)));

        var result = await service.SearchAsync("Production");

        result.Should().ContainSingle().Which.Title.Should().Be("ProductionServer01");
        capturedUserId.Should().Be("owner");
        capturedKeyword.Should().Be("Production");
        capturedQuery!.View.Should().Be(CatalogView.Mine);
        capturedQuery.Limit.Should().Be(20);
    }

    [Fact]
    public async Task SearchAsync_returns_the_items_of_the_paged_result_only()
    {
        var results = Enumerable.Range(0, 25)
            .Select(index => new SearchResultDto { Type = "SERVER", Title = $"Server{index:D2}" })
            .ToArray();

        var service = Service(configure: catalog => catalog
            .Setup(repository => repository.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CatalogPageQuery>(),
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorPageDto<SearchResultDto>(results, "next-cursor", true)));

        var result = await service.SearchAsync("Server");

        result.Should().HaveCount(25);
        result.Select(item => item.Title).Should().BeInAscendingOrder();
    }
}

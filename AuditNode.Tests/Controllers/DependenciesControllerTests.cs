using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class DependenciesControllerTests
{
    private readonly Mock<IDependencyService> _service = new();

    [Fact]
    public async Task Successful_sync_returns_no_content()
    {
        var dto = new SyncDependenciesDto();
        _service.Setup(x => x.SyncDependenciesAsync(dto)).ReturnsAsync(DependencySyncStatus.Success);

        var result = await Controller().SyncDependencies(dto);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Duplicate_sync_returns_conflict()
    {
        var dto = new SyncDependenciesDto();
        _service.Setup(x => x.SyncDependenciesAsync(dto)).ReturnsAsync(DependencySyncStatus.Duplicate);

        var result = await Controller().SyncDependencies(dto);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Unexpected_exception_returns_safe_500()
    {
        const string secret = "connection string";
        var dto = new SyncDependenciesDto();
        _service.Setup(x => x.SyncDependenciesAsync(dto)).ThrowsAsync(new Exception(secret));

        var result = await Controller().SyncDependencies(dto);

        var failure = result.Should().BeOfType<ObjectResult>().Subject;
        failure.StatusCode.Should().Be(500);
        failure.Value!.ToString().Should().NotContain(secret);
    }

    private DependenciesController Controller() => new(_service.Object);
}

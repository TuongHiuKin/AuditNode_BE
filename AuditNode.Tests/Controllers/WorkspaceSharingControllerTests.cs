using System.Security.Claims;
using AuditNode.API.Controllers;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public sealed class WorkspaceSharingControllerTests
{
    [Theory]
    [InlineData("ab", 0, 20)]
    [InlineData("alice", -1, 20)]
    [InlineData("alice", 101, 20)]
    [InlineData("alice", 0, 21)]
    public async Task Options_rejects_invalid_directory_queries(string search, int first, int max)
    {
        var options = new Mock<IWorkspaceShareOptionsService>(MockBehavior.Strict);
        var controller = Controller(options.Object);

        var result = await controller.Options(Guid.NewGuid(), search, first, max);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        options.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Options_allows_empty_search_only_for_non_directory_targets()
    {
        var workspaceId = Guid.NewGuid();
        var options = new Mock<IWorkspaceShareOptionsService>();
        options.Setup(x => x.GetAsync(workspaceId, "actor", string.Empty, 0, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceShareOptionsDto([], [], []));
        var controller = Controller(options.Object);

        var result = await controller.Options(workspaceId, "  ");

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void Options_has_dedicated_rate_limit_policy()
    {
        var attribute = typeof(WorkspaceSharingController).GetMethod(nameof(WorkspaceSharingController.Options))!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        attribute.PolicyName.Should().Be("share-options");
    }

    private static WorkspaceSharingController Controller(IWorkspaceShareOptionsService options)
    {
        var controller = new WorkspaceSharingController(Mock.Of<IWorkspaceSharingService>(), options);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "actor")], "test"))
            }
        };
        return controller;
    }
}

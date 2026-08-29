using System.Security.Claims;
using AuditNode.API.Middleware;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace AuditNode.Tests.Middleware;

public class WorkspaceMiddlewareTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    
    public async Task InvokeAsync_ShouldReturnBadRequest_WhenWorkspaceHeaderIsInvalid(string? headerValue)
    {
        var context = CreateAuthenticatedContext();
        if (headerValue is not null)
        {
            context.Request.Headers["X-Workspace-Id"] = headerValue;
        }

        var service = new Mock<IWorkspaceService>(MockBehavior.Strict);
        var nextCalled = false;
        var middleware = new WorkspaceMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, new TenantProvider(), service.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnNotFound_WhenWorkspaceDoesNotExist()
    {
        var workspaceId = Guid.NewGuid();
        var context = CreateAuthenticatedContext();
        context.Request.Headers["X-Workspace-Id"] = workspaceId.ToString();
        var service = new Mock<IWorkspaceService>();
        service.Setup(x => x.ExistsAsync(workspaceId)).ReturnsAsync(false);

        await new WorkspaceMiddleware(_ => Task.CompletedTask)
            .InvokeAsync(context, new TenantProvider(), service.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        service.Verify(x => x.UserHasAccessAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnForbidden_WhenUserIsNotOwnerOrMember()
    {
        var workspaceId = Guid.NewGuid();
        var context = CreateAuthenticatedContext("user-a");
        context.Request.Headers["X-Workspace-Id"] = workspaceId.ToString();
        var service = new Mock<IWorkspaceService>();
        service.Setup(x => x.ExistsAsync(workspaceId)).ReturnsAsync(true);
        service.Setup(x => x.UserHasAccessAsync(workspaceId, "user-a")).ReturnsAsync(false);

        await new WorkspaceMiddleware(_ => Task.CompletedTask)
            .InvokeAsync(context, new TenantProvider(), service.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetTenantAndContinue_WhenUserHasAccess()
    {
        var workspaceId = Guid.NewGuid();
        var context = CreateAuthenticatedContext("keycloak-user");
        context.Request.Headers["X-Workspace-Id"] = workspaceId.ToString();
        var tenantProvider = new TenantProvider();
        var service = new Mock<IWorkspaceService>();
        service.Setup(x => x.ExistsAsync(workspaceId)).ReturnsAsync(true);
        service.Setup(x => x.UserHasAccessAsync(workspaceId, "keycloak-user")).ReturnsAsync(true);
        var nextCalled = false;

        await new WorkspaceMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }).InvokeAsync(context, tenantProvider, service.Object);

        nextCalled.Should().BeTrue();
        tenantProvider.WorkspaceId.Should().Be(workspaceId);
    }

    [Theory]
    [InlineData("/api/v1/labels/11111111-1111-1111-1111-111111111111/grants")]
    [InlineData("/api/v1/share-links/resolve")]
    public async Task Marked_global_label_policy_endpoints_bypass_legacy_workspace_validation(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new SkipWorkspaceValidationAttribute()),
            "label-policy"));
        var service = new Mock<IWorkspaceService>(MockBehavior.Strict);
        var nextCalled = false;

        await new WorkspaceMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }).InvokeAsync(context, new TenantProvider(), service.Object);

        nextCalled.Should().BeTrue();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unmarked_future_label_route_does_not_bypass_workspace_validation_by_path()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/labels/future-endpoint";
        context.Response.Body = new MemoryStream();
        var nextCalled = false;

        await new WorkspaceMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }).InvokeAsync(context, new TenantProvider(), Mock.Of<IWorkspaceService>());

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public void Marker_is_limited_to_phase_three_controllers_and_not_inventory_labels()
    {
        typeof(AuditNode.API.Controllers.LabelGrantsController)
            .Should().BeDecoratedWith<SkipWorkspaceValidationAttribute>();
        typeof(AuditNode.API.Controllers.ShareLinksController)
            .Should().BeDecoratedWith<SkipWorkspaceValidationAttribute>();
        typeof(AuditNode.API.Controllers.LabelsController)
            .Should().NotBeDecoratedWith<SkipWorkspaceValidationAttribute>();
    }

    private static DefaultHttpContext CreateAuthenticatedContext(string userId = "user-a")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/servers";
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        return context;
    }
}

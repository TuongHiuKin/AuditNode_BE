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

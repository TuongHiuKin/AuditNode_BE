using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class AdminUsersControllerTests
{
    [Fact]
    public void Controller_requires_SystemAdmin_policy() =>
        typeof(AdminUsersController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .Should().ContainSingle(x => x.Policy == "SystemAdminOnly");

    [Fact]
    public async Task Create_maps_identity_conflict_without_exposing_upstream_details()
    {
        var identity = new Mock<IIdentityAdminService>();
        identity.Setup(x => x.CreateUserAsync(It.IsAny<CreateIdentityAdminUserDto>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IdentityConflictException());
        var summaries = new Mock<IWorkspaceUserSummaryService>();
        var controller = new AdminUsersController(identity.Object, summaries.Object, NullLogger<AdminUsersController>.Instance);

        var result = await controller.Create(new("user", "user@example.com", "password"), default);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Roles_maps_last_admin_protection_to_conflict()
    {
        var identity = new Mock<IIdentityAdminService>();
        identity.Setup(x => x.SetSystemAdminAsync("last-admin", false, It.IsAny<CancellationToken>())).ThrowsAsync(new IdentityConflictException());
        var summaries = new Mock<IWorkspaceUserSummaryService>();
        var controller = new AdminUsersController(identity.Object, summaries.Object, NullLogger<AdminUsersController>.Instance);

        var result = await controller.Roles("last-admin", new(false), default);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Status_rejects_whitespace_identifier_without_calling_identity_service()
    {
        var identity = new Mock<IIdentityAdminService>();
        var controller = new AdminUsersController(identity.Object, new Mock<IWorkspaceUserSummaryService>().Object, NullLogger<AdminUsersController>.Instance);

        var result = await controller.Status(" ", new(true), default);

        result.Should().BeOfType<BadRequestResult>();
        identity.Verify(x => x.SetEnabledAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("conflict", 409)]
    [InlineData("configuration", 500)]
    [InlineData("upstream", 503)]
    public async Task Status_normalizes_identity_failures(string failure, int expectedStatus)
    {
        var identity = new Mock<IIdentityAdminService>();
        Exception exception = failure switch
        {
            "conflict" => new IdentityConflictException(),
            "configuration" => new IdentityConfigurationException(),
            _ => new IdentityUpstreamUnavailableException()
        };
        identity.Setup(x => x.SetEnabledAsync("user-id", false, It.IsAny<CancellationToken>())).ThrowsAsync(exception);
        var controller = new AdminUsersController(identity.Object, new Mock<IWorkspaceUserSummaryService>().Object, NullLogger<AdminUsersController>.Instance);

        var result = await controller.Status("user-id", new(false), default);

        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(expectedStatus);
    }

    private static AuditDbContext Context()
    {
        var tenant = new Mock<ITenantProvider>();
        return new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant.Object);
    }
}

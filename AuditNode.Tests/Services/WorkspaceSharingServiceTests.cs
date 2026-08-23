using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public class WorkspaceSharingServiceTests
{
    private static WorkspaceSharingService Service(AuditDbContext context)
    {
        var identities = new Mock<IIdentityAdminService>();
        identities.Setup(x => x.GetUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new IdentityAdminUserDto(id, id, null, true));
        return new WorkspaceSharingService(context, NullLogger<WorkspaceSharingService>.Instance, identities.Object);
    }
    [Fact]
    public async Task GrantAsync_ShouldPersistLabelScopedAuditorForOwner()
    {
        var workspaceId = Guid.NewGuid();
        var labelId = Guid.NewGuid();
        await using var context = CreateContext();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Private", OwnerUserId = "owner" });
        context.Labels.Add(new Label { Id = labelId, WorkspaceId = workspaceId, Key = "env", Value = "staging" });
        await context.SaveChangesAsync();
        var service = Service(context);

        var result = await service.GrantAsync(workspaceId, "owner",
            new UpsertWorkspaceShareDto("auditor", WorkspaceRoles.Auditor, WorkspaceScopeModes.Labels, [labelId]));

        result.Success.Should().BeTrue();
        result.Share!.Version.Should().Be(1);
        (await context.WorkspaceMembers.Include(x => x.Scopes).SingleAsync()).Scopes
            .Should().ContainSingle(x => x.TargetId == labelId && x.ScopeType == WorkspaceScopeTypes.Label);
    }

    [Fact]
    public async Task GrantAsync_ShouldRejectCrossWorkspaceTarget()
    {
        var workspaceId = Guid.NewGuid();
        var otherWorkspaceId = Guid.NewGuid();
        var labelId = Guid.NewGuid();
        await using var context = CreateContext();
        context.Workspaces.AddRange(
            new Workspace { Id = workspaceId, Name = "Private", OwnerUserId = "owner" },
            new Workspace { Id = otherWorkspaceId, Name = "Other", OwnerUserId = "other" });
        context.Labels.Add(new Label { Id = labelId, WorkspaceId = otherWorkspaceId, Key = "env", Value = "private" });
        await context.SaveChangesAsync();

        var result = await Service(context)
            .GrantAsync(workspaceId, "owner", new UpsertWorkspaceShareDto("viewer", WorkspaceRoles.Viewer, WorkspaceScopeModes.Labels, [labelId]));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid");
        context.WorkspaceMembers.Should().BeEmpty();
    }

    [Fact]
    public async Task GrantAsync_ShouldRejectViewerActor()
    {
        var workspaceId = Guid.NewGuid();
        await using var context = CreateContext();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Private", OwnerUserId = "owner" });
        context.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "viewer", Role = WorkspaceRoles.Viewer, ScopeMode = WorkspaceScopeModes.All, InvitedByUserId = "owner" });
        await context.SaveChangesAsync();

        var result = await Service(context)
            .GrantAsync(workspaceId, "viewer", new UpsertWorkspaceShareDto("other", WorkspaceRoles.Viewer, WorkspaceScopeModes.All, []));

        result.ErrorCode.Should().Be("forbidden");
    }

    private static AuditDbContext CreateContext()
    {
        var tenant = new Mock<ITenantProvider>();
        return new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant.Object);
    }
}

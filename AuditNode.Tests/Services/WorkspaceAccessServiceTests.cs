using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public class WorkspaceAccessServiceTests
{
    [Fact]
    public async Task ResolveAsync_ShouldReturnScopedAuditorCapabilities()
    {
        var workspaceId = Guid.NewGuid();
        var labelId = Guid.NewGuid();
        await using var context = CreateContext();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Owned", OwnerUserId = "owner" });
        context.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "auditor", Role = WorkspaceRoles.Auditor, ScopeMode = WorkspaceScopeModes.Labels, InvitedByUserId = "owner" });
        context.WorkspaceMemberScopes.Add(new WorkspaceMemberScope { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = "auditor", ScopeType = WorkspaceScopeTypes.Label, TargetId = labelId, CreatedByUserId = "owner" });
        await context.SaveChangesAsync();

        var access = await new WorkspaceAccessService(context).ResolveAsync(workspaceId, "auditor");

        access.Should().NotBeNull();
        access!.EffectiveRole.Should().Be(WorkspaceRoles.Auditor);
        access.Capabilities.CanWriteInventory.Should().BeTrue();
        access.Capabilities.CanManageShares.Should().BeFalse();
        access.Scope.Labels.Select(x => x.Id).Should().ContainSingle().Which.Should().Be(labelId);
    }

    [Fact]
    public async Task ResolveAsync_ShouldNotGiveSystemUserImplicitWorkspaceAccess()
    {
        var workspaceId = Guid.NewGuid();
        await using var context = CreateContext();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Private", OwnerUserId = "owner" });
        await context.SaveChangesAsync();

        (await new WorkspaceAccessService(context).ResolveAsync(workspaceId, "system-admin")).Should().BeNull();
    }

    private static AuditDbContext CreateContext()
    {
        var tenant = new Mock<ITenantProvider>();
        return new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant.Object);
    }
}

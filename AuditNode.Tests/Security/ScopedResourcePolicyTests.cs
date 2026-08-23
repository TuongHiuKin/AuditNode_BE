using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AuditNode.Tests.Security;

public class ScopedResourcePolicyTests
{
    [Fact]
    public async Task LabelScopedViewer_ShouldReadOnlyMatchingResourceAndNeverWrite()
    {
        var workspaceId = Guid.NewGuid();
        var labelId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        await using var context = CreateContext();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Private", OwnerUserId = "owner" });
        context.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "viewer", Role = WorkspaceRoles.Viewer, ScopeMode = WorkspaceScopeModes.Labels, InvitedByUserId = "owner" });
        context.WorkspaceMemberScopes.Add(new WorkspaceMemberScope { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = "viewer", ScopeType = WorkspaceScopeTypes.Label, TargetId = labelId, CreatedByUserId = "owner" });
        context.ServerLabels.Add(new ServerLabel { WorkspaceId = workspaceId, ServerId = serverId, LabelId = labelId });
        await context.SaveChangesAsync();
        var policy = new ScopedResourcePolicy(context, new WorkspaceAccessService(context));

        (await policy.CanReadAsync(workspaceId, "viewer", "server", serverId)).Should().BeTrue();
        (await policy.CanReadAsync(workspaceId, "viewer", "server", Guid.NewGuid())).Should().BeFalse();
        (await policy.CanWriteAsync(workspaceId, "viewer", "server", serverId)).Should().BeFalse();
    }

    [Fact]
    public async Task FrameScopedAuditor_ShouldEditDescendantButCannotCreateInV1()
    {
        var workspaceId = Guid.NewGuid();
        var frameId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        await using var context = CreateContext();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Private", OwnerUserId = "owner" });
        context.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "auditor", Role = WorkspaceRoles.Auditor, ScopeMode = WorkspaceScopeModes.Frames, InvitedByUserId = "owner" });
        context.WorkspaceMemberScopes.Add(new WorkspaceMemberScope { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = "auditor", ScopeType = WorkspaceScopeTypes.Frame, TargetId = frameId, CreatedByUserId = "owner" });
        context.TopologyNodes.AddRange(
            new TopologyNode { Id = frameId, WorkspaceId = workspaceId, NodeType = "frame", Label = "Payment" },
            new TopologyNode { Id = Guid.NewGuid(), WorkspaceId = workspaceId, NodeType = "server", Label = "API", ParentNodeId = frameId, ReferenceId = serverId });
        await context.SaveChangesAsync();
        var policy = new ScopedResourcePolicy(context, new WorkspaceAccessService(context));

        (await policy.CanWriteAsync(workspaceId, "auditor", "server", serverId)).Should().BeTrue();
        (await policy.CanCreateAsync(workspaceId, "auditor", "server", Array.Empty<LabelDto>())).Should().BeFalse();
    }

    private static AuditDbContext CreateContext()
    {
        var tenant = new Mock<ITenantProvider>();
        return new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant.Object);
    }
}

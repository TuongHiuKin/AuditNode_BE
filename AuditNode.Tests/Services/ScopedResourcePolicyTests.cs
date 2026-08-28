using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class ScopedResourcePolicyTests
{
    [Fact]
    public async Task FrameScopedApplicationIds_are_application_ids_not_deployment_ids()
    {
        var workspaceId = Guid.NewGuid();
        var frameId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var mappingId = Guid.NewGuid();
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(item => item.WorkspaceId).Returns(workspaceId);
        await using var context = new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant.Object);
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Frame scope", OwnerUserId = "owner" });
        context.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = "auditor",
            Role = WorkspaceRoles.Auditor,
            ScopeMode = WorkspaceScopeModes.Frames,
            InvitedByUserId = "owner",
            Scopes =
            [
                new WorkspaceMemberScope
                {
                    Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = "auditor",
                    ScopeType = WorkspaceScopeTypes.Frame, TargetId = frameId, CreatedByUserId = "owner"
                }
            ]
        });
        context.TopologyNodes.AddRange(
            new TopologyNode { Id = frameId, NodeType = "frame", Label = "frame" },
            new TopologyNode { Id = Guid.NewGuid(), NodeType = "application", Label = "app", ParentNodeId = frameId, ReferenceId = mappingId });
        context.PortMappings.Add(new PortMapping
        {
            Id = mappingId, AppId = appId, ServerId = Guid.NewGuid(), PortNumber = 8080
        });
        await context.SaveChangesAsync();
        var access = new WorkspaceAccessService(context);

        var policy = new ScopedResourcePolicy(context, access);
        var readable = await policy.GetReadableIdsAsync(workspaceId, "auditor", "application");

        readable.Should().BeEquivalentTo([appId]);
        readable.Should().NotContain(mappingId);
        (await policy.CanReadAsync(workspaceId, "auditor", "application", appId)).Should().BeTrue();
    }
}

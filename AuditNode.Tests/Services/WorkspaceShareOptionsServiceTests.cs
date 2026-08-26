using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public class WorkspaceShareOptionsServiceTests
{
    [Fact]
    public async Task Owner_receives_only_active_users_and_workspace_owned_targets()
    {
        var workspaceId = Guid.NewGuid();
        var other = Guid.NewGuid();
        await using var context = Context();
        context.Workspaces.AddRange(new Workspace { Id = workspaceId, Name = "Mine", OwnerUserId = "owner" }, new Workspace { Id = other, Name = "Other", OwnerUserId = "other" });
        context.Labels.AddRange(new Label { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Key = "env", Value = "prod" }, new Label { Id = Guid.NewGuid(), WorkspaceId = other, Key = "secret", Value = "other" });
        context.TopologyNodes.AddRange(new TopologyNode { Id = Guid.NewGuid(), WorkspaceId = workspaceId, NodeType = "frame", Label = "Payment" }, new TopologyNode { Id = Guid.NewGuid(), WorkspaceId = other, NodeType = "frame", Label = "Hidden" });
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>();
        identities.Setup(x => x.ListUsersAsync("aud", 0, 100, It.IsAny<CancellationToken>())).ReturnsAsync([
            new("active", "auditor", "a@example.com", true), new("disabled", "disabled", null, false)]);

        var result = await new WorkspaceShareOptionsService(context, identities.Object).GetAsync(workspaceId, "owner", "aud", 0, 20);

        result.Should().NotBeNull();
        result!.Users.Should().ContainSingle(x => x.Id == "active");
        result.Labels.Should().ContainSingle(x => x.DisplayName == "env:prod");
        result.Frames.Should().ContainSingle(x => x.DisplayName == "Payment");
    }

    [Fact]
    public async Task Empty_search_returns_targets_without_enumerating_identity_directory()
    {
        var workspaceId = Guid.NewGuid();
        await using var context = Context();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Mine", OwnerUserId = "owner" });
        context.Labels.Add(new Label { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Key = "env", Value = "prod" });
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>(MockBehavior.Strict);

        var result = await new WorkspaceShareOptionsService(context, identities.Object)
            .GetAsync(workspaceId, "owner", "  ", 0, 20);

        result.Should().NotBeNull();
        result!.Users.Should().BeEmpty();
        result.Labels.Should().ContainSingle();
        identities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Search_prioritizes_exact_then_prefix_and_caps_identity_page()
    {
        var workspaceId = Guid.NewGuid();
        await using var context = Context();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Mine", OwnerUserId = "owner" });
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>();
        var candidates = Enumerable.Range(0, 25)
            .Select(i => new IdentityAdminUserDto($"contains-{i}", $"team-alice-{i:D2}", null, true))
            .Append(new IdentityAdminUserDto("prefix", "alice.ops", null, true))
            .Append(new IdentityAdminUserDto("exact", "Alice", null, true))
            .Append(new IdentityAdminUserDto("disabled", "alice.disabled", null, false))
            .ToArray();
        identities.Setup(x => x.ListUsersAsync("alice", 0, 100, It.IsAny<CancellationToken>())).ReturnsAsync(candidates);

        var result = await new WorkspaceShareOptionsService(context, identities.Object)
            .GetAsync(workspaceId, "owner", "alice", 0, 99);

        result!.Users.Should().HaveCount(20);
        result.Users.Select(x => x.Id).Should().StartWith("exact", "prefix");
        identities.Verify(x => x.ListUsersAsync("alice", 0, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pagination_is_applied_after_global_match_ranking()
    {
        var workspaceId = Guid.NewGuid();
        await using var context = Context();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Mine", OwnerUserId = "owner" });
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>();
        identities.Setup(x => x.ListUsersAsync("alice", 0, 100, It.IsAny<CancellationToken>())).ReturnsAsync([
            new("contains", "team-alice", null, true),
            new("prefix", "alice.ops", null, true),
            new("exact", "Alice", null, true)]);

        var result = await new WorkspaceShareOptionsService(context, identities.Object)
            .GetAsync(workspaceId, "owner", "alice", 1, 1);

        result!.Users.Should().ContainSingle().Which.Id.Should().Be("prefix");
    }

    [Fact]
    public async Task Workspace_admin_can_query_identity_directory()
    {
        var workspaceId = Guid.NewGuid();
        await using var context = Context();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Mine", OwnerUserId = "owner" });
        context.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = "manager",
            Role = WorkspaceRoles.Admin,
            ScopeMode = WorkspaceScopeModes.All,
            InvitedByUserId = "owner"
        });
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>();
        identities.Setup(x => x.ListUsersAsync("alice", 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("alice", "alice", null, true)]);

        var result = await new WorkspaceShareOptionsService(context, identities.Object)
            .GetAsync(workspaceId, "manager", "alice", 0, 20);

        result.Should().NotBeNull();
        result!.Users.Should().ContainSingle(x => x.Id == "alice");
    }

    [Fact]
    public async Task Auditor_cannot_query_identity_directory()
    {
        var workspaceId = Guid.NewGuid();
        await using var context = Context();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Mine", OwnerUserId = "owner" });
        context.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = "auditor",
            Role = WorkspaceRoles.Auditor,
            ScopeMode = WorkspaceScopeModes.All,
            InvitedByUserId = "owner"
        });
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>(MockBehavior.Strict);

        var result = await new WorkspaceShareOptionsService(context, identities.Object)
            .GetAsync(workspaceId, "auditor", "alice", 0, 20);

        result.Should().BeNull();
        identities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Non_manager_cannot_query_identity_directory()
    {
        var workspaceId = Guid.NewGuid();
        await using var context = Context();
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Mine", OwnerUserId = "owner" });
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>(MockBehavior.Strict);

        var result = await new WorkspaceShareOptionsService(context, identities.Object)
            .GetAsync(workspaceId, "viewer", "alice", 0, 20);

        result.Should().BeNull();
        identities.VerifyNoOtherCalls();
    }

    private static AuditDbContext Context()
    {
        var tenant = new Mock<ITenantProvider>();
        return new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant.Object);
    }
}

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
        identities.Setup(x => x.ListUsersAsync("aud", 0, 20, It.IsAny<CancellationToken>())).ReturnsAsync([
            new("active", "auditor", "a@example.com", true), new("disabled", "disabled", null, false)]);

        var result = await new WorkspaceShareOptionsService(context, identities.Object).GetAsync(workspaceId, "owner", "aud", 0, 20);

        result.Should().NotBeNull();
        result!.Users.Should().ContainSingle(x => x.Id == "active");
        result.Labels.Should().ContainSingle(x => x.DisplayName == "env:prod");
        result.Frames.Should().ContainSingle(x => x.DisplayName == "Payment");
    }

    private static AuditDbContext Context()
    {
        var tenant = new Mock<ITenantProvider>();
        return new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant.Object);
    }
}

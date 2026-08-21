using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace AuditNode.Tests.Services;

public class TenantProviderTests
{
    [Fact]
    public void SetWorkspaceId_ShouldSetWorkspaceId_WhenGuidIsValid()
    {
        var provider = new TenantProvider();
        var workspaceId = Guid.NewGuid();
        provider.SetWorkspaceId(workspaceId.ToString());
        provider.WorkspaceId.Should().Be(workspaceId);
    }

    [Fact]
    public void SetWorkspaceId_ShouldNotSetWorkspaceId_WhenGuidIsInvalid()
    {
        var provider = new TenantProvider();
        provider.SetWorkspaceId("invalid-guid");
        provider.WorkspaceId.Should().BeNull();
    }

    [Fact]
    public void SetWorkspaceId_ShouldNotSetWorkspaceId_WhenGuidIsNull()
    {
        var provider = new TenantProvider();
        provider.SetWorkspaceId(null);
        provider.WorkspaceId.Should().BeNull();
    }

    [Fact]
    public void SetWorkspaceId_ShouldAllowEmptyGuid()
    {
        var provider = new TenantProvider { WorkspaceId = Guid.NewGuid() };
        provider.SetWorkspaceId(Guid.Empty.ToString());
        provider.WorkspaceId.Should().Be(Guid.Empty);
    }
}


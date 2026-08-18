using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace AuditNode.Tests.Services;

public class TenantProviderTests
{
    [Fact]
    public void SetWorkspaceId_ShouldSetWorkspaceId_WhenGuidIsValid()
    {
        // Arrange
        var provider = new TenantProvider();
        var workspaceId = Guid.NewGuid();

        // Act
        provider.SetWorkspaceId(workspaceId.ToString());

        // Assert
        provider.WorkspaceId.Should().Be(workspaceId);
    }

    [Fact]
    public void SetWorkspaceId_ShouldNotSetWorkspaceId_WhenGuidIsInvalid()
    {
        // Arrange
        var provider = new TenantProvider();

        // Act
        provider.SetWorkspaceId("invalid-guid");

        // Assert
        provider.WorkspaceId.Should().BeNull();
    }

    [Fact]
    public void SetWorkspaceId_ShouldNotSetWorkspaceId_WhenGuidIsNull()
    {
        // Arrange
        var provider = new TenantProvider();

        // Act
        provider.SetWorkspaceId(null);

        // Assert
        provider.WorkspaceId.Should().BeNull();
    }

    [Fact]
    public void SetWorkspaceId_ShouldRejectEmptyGuidAndClearPreviousValue()
    {
        var provider = new TenantProvider();
        provider.SetWorkspaceId(Guid.NewGuid().ToString());

        provider.SetWorkspaceId(Guid.Empty.ToString());

        provider.WorkspaceId.Should().BeNull();
    }
}

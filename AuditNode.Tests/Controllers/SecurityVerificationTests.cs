using AuditNode.API.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class SecurityVerificationTests
{
    [Theory]
    [InlineData(typeof(ApplicationsController))]
    [InlineData(typeof(AnalyticsController))]
    [InlineData(typeof(DatacentersController))]
    [InlineData(typeof(DependenciesController))]
    [InlineData(typeof(InfrastructureController))]
    [InlineData(typeof(InventoryImportController))]
    [InlineData(typeof(InventorySearchController))]
    [InlineData(typeof(ServersController))]
    [InlineData(typeof(TopologyController))]
    public void Controller_Should_Have_AuthorizeAttribute(Type controllerType)
    {
        // Assert
        controllerType.Should().BeDerivedFrom<ControllerBase>();
        controllerType.Should().BeDecoratedWith<AuthorizeAttribute>();
    }

    [Fact]
    public void All_Controllers_In_API_Namespace_Should_Be_Tested()
    {
        // Arrange
        var assembly = typeof(ApplicationsController).Assembly;
        var controllerTypes = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract && t != typeof(AuthController))
            .ToList();

        // Act & Assert
        // This ensures we didn't miss any new controllers in our Theory above
        controllerTypes.Should().HaveCount(9, "We expect exactly 9 controllers to be protected");
    }
}

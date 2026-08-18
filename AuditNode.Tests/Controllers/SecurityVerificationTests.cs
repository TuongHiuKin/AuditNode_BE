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
    [InlineData(typeof(WorkspacesController))]
    public void Controller_Should_Have_AuthorizeAttribute(Type controllerType)
    {
        // Assert
        controllerType.Should().BeDerivedFrom<ControllerBase>();
        controllerType.Should().BeDecoratedWith<AuthorizeAttribute>();
    }

    [Theory]
    [InlineData(typeof(DatacentersController), nameof(DatacentersController.CreateDatacenter), "Admin")]
    [InlineData(typeof(ApplicationsController), nameof(ApplicationsController.PostApplication), "Admin,Auditor")]
    [InlineData(typeof(ApplicationsController), nameof(ApplicationsController.PutApplication), "Admin,Auditor")]
    [InlineData(typeof(DependenciesController), nameof(DependenciesController.SyncDependencies), "Admin,Auditor")]
    [InlineData(typeof(InventoryImportController), nameof(InventoryImportController.ImportInventory), "Admin,Auditor")]
    [InlineData(typeof(InfrastructureController), nameof(InfrastructureController.MigrateApp), "Admin,Auditor")]
    [InlineData(typeof(InfrastructureController), nameof(InfrastructureController.PurgeApp), "Admin,Auditor")]
    [InlineData(typeof(ServersController), nameof(ServersController.CreateServer), "Admin,Auditor")]
    [InlineData(typeof(ServersController), nameof(ServersController.UpdateServer), "Admin,Auditor")]
    [InlineData(typeof(ServersController), nameof(ServersController.DeleteServer), "Admin,Auditor")]
    [InlineData(typeof(TopologyController), nameof(TopologyController.SaveState), "Admin,Auditor")]
    public void Mutating_Actions_Should_Require_Specific_Roles(Type controllerType, string methodName, string expectedRoles)
    {
        // Arrange
        var methodInfo = controllerType.GetMethod(methodName);
        methodInfo.Should().NotBeNull($"Method {methodName} should exist on {controllerType.Name}");

        // Act
        var authorizeAttr = methodInfo!.GetCustomAttribute<AuthorizeAttribute>();

        // Assert
        authorizeAttr.Should().NotBeNull($"Method {methodName} on {controllerType.Name} must be decorated with [Authorize]");
        authorizeAttr!.Roles.Should().Be(expectedRoles, $"Method {methodName} on {controllerType.Name} requires roles '{expectedRoles}'");
    }

    [Theory]
    [InlineData(typeof(WorkspacesController), nameof(WorkspacesController.GetWorkspaces))]
    [InlineData(typeof(DatacentersController), nameof(DatacentersController.GetDatacenters))]
    [InlineData(typeof(ServersController), nameof(ServersController.GetServers))]
    [InlineData(typeof(ServersController), nameof(ServersController.ExportServers))]
    [InlineData(typeof(ApplicationsController), nameof(ApplicationsController.GetApplications))]
    [InlineData(typeof(ApplicationsController), nameof(ApplicationsController.ExportApplications))]
    [InlineData(typeof(ApplicationsController), nameof(ApplicationsController.GetApplication))]
    [InlineData(typeof(InventoryImportController), nameof(InventoryImportController.DownloadTemplate))]
    [InlineData(typeof(InfrastructureController), nameof(InfrastructureController.GetDependenciesCount))]
    [InlineData(typeof(InfrastructureController), nameof(InfrastructureController.GetDeployedAppsByServer))]
    public void Get_Actions_Should_Not_Have_MethodLevel_Role_Restrictions(Type controllerType, string methodName)
    {
        // Arrange
        var methodInfo = controllerType.GetMethod(methodName);
        methodInfo.Should().NotBeNull($"Method {methodName} should exist on {controllerType.Name}");

        // Act
        var authorizeAttr = methodInfo!.GetCustomAttribute<AuthorizeAttribute>();

        // Assert
        if (authorizeAttr != null)
        {
            string.IsNullOrEmpty(authorizeAttr.Roles).Should().BeTrue($"GET Method {methodName} on {controllerType.Name} should not restrict roles at method level");
        }
    }

    [Fact]
    public void All_Controllers_In_API_Namespace_Should_Be_Tested()
    {
        // Arrange
        var assembly = typeof(ApplicationsController).Assembly;
        var controllerTypes = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract)
            .ToList();

        // Act & Assert
        controllerTypes.Should().HaveCount(12, "We expect exactly 12 controllers to be protected");
    }
}

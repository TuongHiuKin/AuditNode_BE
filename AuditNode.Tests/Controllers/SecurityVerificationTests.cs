using AuditNode.API.Controllers;
using AuditNode.API.Security;
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
    [InlineData(typeof(AdminUsersController))]
    public void Controller_Should_Have_AuthorizeAttribute(Type controllerType)
    {
        // Assert
        controllerType.Should().BeDerivedFrom<ControllerBase>();
        controllerType.Should().BeDecoratedWith<AuthorizeAttribute>();
    }

    [Theory]
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
    public void Controller_security_is_policy_based_with_an_explicit_anonymous_action_allowlist()
    {
        var allowedAnonymousActions = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{nameof(AuthController)}.{nameof(AuthController.Login)}",
            $"{nameof(AuthController)}.{nameof(AuthController.Register)}",
            $"{nameof(AuthController)}.{nameof(AuthController.Refresh)}",
            $"{nameof(AuthController)}.{nameof(AuthController.Logout)}",
            $"{nameof(ShareLinksController)}.{nameof(ShareLinksController.Resolve)}",
            $"{nameof(ShareLinksController)}.{nameof(ShareLinksController.Browse)}"
        };
        var controllerTypes = typeof(ApplicationsController).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(ControllerBase)) && !type.IsAbstract)
            .ToList();

        controllerTypes.All(type => type.GetCustomAttribute<AuthorizeAttribute>() != null)
            .Should().BeTrue("every API controller is protected by default");

        var anonymousActions = controllerTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                .Select(method => $"{type.Name}.{method.Name}"))
            .ToHashSet(StringComparer.Ordinal);
        anonymousActions.Should().BeEquivalentTo(allowedAnonymousActions);
    }

}

using AuditNode.API.Security;
using FluentAssertions;
using System.Security.Claims;
using Xunit;

namespace AuditNode.Tests.Security;

public class KeycloakRoleClaimsTransformationTests
{
    private readonly KeycloakRoleClaimsTransformation _transformation;

    public KeycloakRoleClaimsTransformationTests()
    {
        _transformation = new KeycloakRoleClaimsTransformation();
    }

    [Fact]
    public async Task TransformAsync_Should_ReturnOriginalPrincipal_When_PrincipalIsNull()
    {
        // Act
        var result = await _transformation.TransformAsync(null!);

        // Assert
        result.Should().NotBeNull();
        result.Identity.Should().BeNull();
    }

    [Fact]
    public async Task TransformAsync_Should_NotModifyPrincipal_When_IdentityIsNotAuthenticated()
    {
        // Arrange
        var identity = new ClaimsIdentity(); // Not authenticated without authenticationType
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await _transformation.TransformAsync(principal);

        // Assert
        result.Claims.Should().BeEmpty();
    }

    [Fact]
    public async Task TransformAsync_Should_NotModifyPrincipal_When_NoRealmAccessClaimExists()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "user1") }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await _transformation.TransformAsync(principal);

        // Assert
        result.FindAll(ClaimTypes.Role).Should().BeEmpty();
    }

    [Fact]
    public async Task TransformAsync_Should_AddRoleClaims_When_RealmAccessContainsRolesObject()
    {
        // Arrange
        var json = "{\"roles\": [\"Admin\", \"Auditor\"]}";
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user1"),
            new Claim("realm_access", json)
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await _transformation.TransformAsync(principal);

        // Assert
        var roleClaims = result.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        roleClaims.Should().HaveCount(2);
        roleClaims.Should().Contain("Admin");
        roleClaims.Should().Contain("Auditor");
    }

    [Fact]
    public async Task TransformAsync_Should_AddRoleClaims_When_RealmAccessIsDirectArray()
    {
        // Arrange
        var json = "[\"Admin\", \"Auditor\"]";
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("realm_access", json)
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await _transformation.TransformAsync(principal);

        // Assert
        var roleClaims = result.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        roleClaims.Should().HaveCount(2);
        roleClaims.Should().Contain("Admin");
        roleClaims.Should().Contain("Auditor");
    }

    [Fact]
    public async Task TransformAsync_Should_NotAddDuplicateRoles_When_RoleClaimAlreadyExists()
    {
        // Arrange
        var json = "{\"roles\": [\"Admin\", \"Auditor\"]}";
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("realm_access", json)
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await _transformation.TransformAsync(principal);

        // Assert
        var roleClaims = result.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        roleClaims.Should().HaveCount(2, "because Admin already existed and shouldn't be duplicated");
        roleClaims.Should().Contain("Admin");
        roleClaims.Should().Contain("Auditor");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ invalid json... ")]
    [InlineData("{\"roles\": \"not-an-array\"}")]
    public async Task TransformAsync_Should_HandleSafely_When_RealmAccessIsInvalidOrEmpty(string invalidJson)
    {
        // Arrange
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("realm_access", invalidJson)
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await _transformation.TransformAsync(principal);

        // Assert
        result.FindAll(ClaimTypes.Role).Should().BeEmpty();
    }
}

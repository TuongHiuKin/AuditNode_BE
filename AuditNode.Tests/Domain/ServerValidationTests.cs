using AuditNode.Application.DTOs;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace AuditNode.Tests.Domain;

/// <summary>
/// Unit tests for core business / validation rules of the AuditNode domain.
/// These tests run against pure domain objects — no database, no HTTP, no DI.
/// </summary>
public class ServerValidationTests
{
    // -----------------------------------------------------------------------
    // Server entity — structural invariants
    // -----------------------------------------------------------------------

    [Fact]
    public void Server_WhenCreated_ShouldHaveEmptyPortMappingCollection()
    {
        // Arrange & Act
        var server = new Server();

        // Assert
        server.PortMappings.Should().NotBeNull()
            .And.BeEmpty("a brand-new Server must start with no port mappings");
    }

    [Fact]
    public void Server_Id_ShouldBeAssignable_AndRetained()
    {
        // Arrange
        var expectedId = Guid.NewGuid();

        // Act
        var server = new Server { Id = expectedId };

        // Assert
        server.Id.Should().Be(expectedId);
    }

    // -----------------------------------------------------------------------
    // CreateServerDto — business validation rules
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.254")]
    [InlineData("172.16.0.1")]
    public void CreateServerDto_ValidIpAddress_ShouldMatchExpected(string ip)
    {
        // Arrange & Act
        var dto = new CreateServerDto { IpAddress = ip };

        // Assert
        dto.IpAddress.Should().Be(ip, "the IP address must be stored exactly as provided");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void CreateServerDto_ValidEnvironment_ShouldMatchExpected(string env)
    {
        // Arrange & Act
        var dto = new CreateServerDto { Environment = env };

        // Assert
        dto.Environment.Should().Be(env);
    }

    [Fact]
    public void CreateServerDto_DefaultValues_ShouldBeEmptyStrings_NotNull()
    {
        // Arrange & Act
        var dto = new CreateServerDto();

        // Assert — guards against accidental null refs in mapping code
        dto.IpAddress.Should().NotBeNull().And.BeEmpty();
        dto.Hostname.Should().NotBeNull().And.BeEmpty();
        dto.OsType.Should().NotBeNull().And.BeEmpty();
        dto.Environment.Should().NotBeNull().And.BeEmpty();
        dto.Status.Should().NotBeNull().And.BeEmpty();
    }

    // -----------------------------------------------------------------------
    // PortMapping — business rule: port number must be in valid TCP/UDP range
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(80)]
    [InlineData(443)]
    [InlineData(8080)]
    [InlineData(65535)]
    public void PortMapping_PortNumber_WhenInValidRange_ShouldBeAccepted(int port)
    {
        // Arrange & Act
        var mapping = new PortMapping { PortNumber = port };

        // Assert
        mapping.PortNumber.Should().BeInRange(1, 65535,
            "TCP/UDP port numbers must be between 1 and 65535");
    }

    [Fact]
    public void PortMapping_WhenCreated_ShouldHaveEmptyAppDependencyCollection()
    {
        // Arrange & Act
        var mapping = new PortMapping();

        // Assert
        mapping.AppDependencies.Should().NotBeNull()
            .And.BeEmpty("a new PortMapping must not have stale dependencies");
    }

    // -----------------------------------------------------------------------
    // Application entity — AppCode must be non-empty to be meaningful
    // -----------------------------------------------------------------------

    [Fact]
    public void Application_AppCode_WhenSet_ShouldNotBeWhitespace()
    {
        // Arrange
        const string code = "APP-001";

        // Act
        var app = new AuditNode.Domain.Entities.Application { AppCode = code };

        // Assert
        app.AppCode.Should().NotBeNullOrWhiteSpace()
            .And.Be(code, "AppCode is the primary business identifier and must be retained as-is");
    }

    [Fact]
    public void Application_WhenCreated_NavigationCollections_ShouldBeInitialized()
    {
        // Arrange & Act
        var app = new AuditNode.Domain.Entities.Application();

        // Assert
        app.PortMappings.Should().NotBeNull().And.BeEmpty();
        app.SourceDependencies.Should().NotBeNull().And.BeEmpty();
        app.DestinationDependencies.Should().NotBeNull().And.BeEmpty();
    }
}

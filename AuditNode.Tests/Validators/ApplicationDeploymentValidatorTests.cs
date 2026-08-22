using AuditNode.Application.DTOs;
using AuditNode.Application.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace AuditNode.Tests.Validators;

public class ApplicationDeploymentValidatorTests
{
    [Fact]
    public void Update_network_fields_require_explicit_port_mapping_id()
    {
        var dto = ValidUpdate();
        dto.TargetServerId = Guid.NewGuid();
        dto.PortNumber = 443;

        new UpdateApplicationDtoValidator().TestValidate(dto)
            .ShouldHaveValidationErrorFor(x => x.PortMappingId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Migration_rejects_out_of_range_port(int port)
    {
        var dto = new MigrateAppDto
        {
            PortMappingId = Guid.NewGuid(), TargetServerId = Guid.NewGuid(), NewPortNumber = port
        };

        new MigrateAppDtoValidator().TestValidate(dto)
            .ShouldHaveValidationErrorFor(x => x.NewPortNumber);
    }

    [Fact]
    public void Migration_rejects_empty_identifiers()
    {
        var result = new MigrateAppDtoValidator().TestValidate(new MigrateAppDto { NewPortNumber = 443 });

        result.ShouldHaveValidationErrorFor(x => x.PortMappingId);
        result.ShouldHaveValidationErrorFor(x => x.TargetServerId);
    }

    private static UpdateApplicationDto ValidUpdate() => new()
    {
        AppName = "App", OwnerTeam = "Team", Risk = "LOW", Icon = "", TechStack = ""
    };
}

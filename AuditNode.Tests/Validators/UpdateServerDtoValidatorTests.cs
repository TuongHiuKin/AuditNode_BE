using AuditNode.Application.DTOs;
using AuditNode.Application.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace AuditNode.Tests.Validators;

public class UpdateServerDtoValidatorTests
{
    private readonly UpdateServerDtoValidator _validator = new();

    [Theory]
    [InlineData("10.20.30.40", false)]
    [InlineData("999.20.30.40", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("", true)]
    public void Ip_address_must_be_a_valid_ipv4_address(string ipAddress, bool shouldHaveError)
    {
        var result = _validator.TestValidate(ValidDto(ipAddress));

        if (shouldHaveError)
            result.ShouldHaveValidationErrorFor(x => x.IpAddress);
        else
            result.ShouldNotHaveValidationErrorFor(x => x.IpAddress);
    }

    [Fact]
    public void Datacenter_id_cannot_be_empty()
    {
        var dto = ValidDto("10.20.30.40");
        dto.DatacenterId = Guid.Empty;

        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.DatacenterId);
    }

    [Fact]
    public void Label_key_cannot_be_empty()
    {
        var dto = ValidDto("10.20.30.40");
        dto.Labels = [new LabelDto { Key = "", Value = "v" }];

        _validator.TestValidate(dto).ShouldHaveValidationErrorFor("Labels[0].Key");
    }

    private static UpdateServerDto ValidDto(string ipAddress) => new()
    {
        DatacenterId = Guid.NewGuid(),
        IpAddress = ipAddress,
        Hostname = "srv-01",
        OsType = "Linux",
        Environment = "Production",
        Status = "Active"
    };
}

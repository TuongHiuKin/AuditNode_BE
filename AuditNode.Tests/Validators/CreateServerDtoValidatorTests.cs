using AuditNode.Application.DTOs;
using AuditNode.Application.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace AuditNode.Tests.Validators;

public class CreateServerDtoValidatorTests
{
    private readonly CreateServerDtoValidator _validator;

    public CreateServerDtoValidatorTests()
    {
        _validator = new CreateServerDtoValidator();
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    public void Should_Not_Have_Error_When_IpAddress_Is_Valid(string ip)
    {
        var model = new CreateServerDto { IpAddress = ip };
        var result = _validator.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.IpAddress);
    }

    [Theory]
    [InlineData("invalid-ip")]
    [InlineData("192.168.1")]
    [InlineData("256.256.256.256")]
    [InlineData("2001:db8::1")]
    public void Should_Have_Error_When_IpAddress_Is_Invalid(string ip)
    {
        var model = new CreateServerDto { IpAddress = ip };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.IpAddress);
    }

    [Fact]
    public void Should_Have_Error_When_Hostname_Is_Empty()
    {
        var model = new CreateServerDto { Hostname = "" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Hostname);
    }

    [Fact]
    public void Should_Have_Error_When_DatacenterId_Is_Empty()
    {
        var model = new CreateServerDto { DatacenterId = Guid.Empty };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.DatacenterId);
    }

    [Fact]
    public void Should_Have_Error_When_Label_Key_Or_Value_Is_Empty()
    {
        var model = new CreateServerDto { Labels = [new LabelDto { Key = "", Value = "v" }] };
        _validator.TestValidate(model).ShouldHaveValidationErrorFor("Labels[0].Key");
    }
}

using AuditNode.Application.DTOs;
using AuditNode.Application.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace AuditNode.Tests.Validators;

public class CreateApplicationDtoValidatorTests
{
    private readonly CreateApplicationDtoValidator _validator;

    public CreateApplicationDtoValidatorTests()
    {
        _validator = new CreateApplicationDtoValidator();
    }

    [Fact]
    public void Should_Have_Error_When_AppCode_Is_Empty()
    {
        var model = new CreateApplicationDto { AppCode = "" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.AppCode);
    }

    [Fact]
    public void Should_Have_Error_When_AppName_Is_Empty()
    {
        var model = new CreateApplicationDto { AppName = "" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.AppName);
    }

    [Fact]
    public void Should_Have_Error_When_OwnerTeam_Is_Empty()
    {
        var model = new CreateApplicationDto { OwnerTeam = "" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.OwnerTeam);
    }

    [Fact]
    public void Should_Not_Have_Error_When_Valid()
    {
        var model = new CreateApplicationDto 
        { 
            AppCode = "APP01", 
            AppName = "Test App", 
            OwnerTeam = "Team A" 
        };
        var result = _validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }
}

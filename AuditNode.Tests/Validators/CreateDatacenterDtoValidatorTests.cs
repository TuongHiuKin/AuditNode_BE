using AuditNode.Application.DTOs;
using AuditNode.Application.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace AuditNode.Tests.Validators;

public class CreateDatacenterDtoValidatorTests
{
    private readonly CreateDatacenterDtoValidator _validator;

    public CreateDatacenterDtoValidatorTests()
    {
        _validator = new CreateDatacenterDtoValidator();
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Empty()
    {
        var model = new CreateDatacenterDto { Name = "" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Have_Error_When_Location_Is_Empty()
    {
        var model = new CreateDatacenterDto { Location = "" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Location);
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Too_Long()
    {
        var model = new CreateDatacenterDto { Name = new string('a', 101) };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Not_Have_Error_When_Valid()
    {
        var model = new CreateDatacenterDto { Name = "DC 1", Location = "US" };
        var result = _validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }
}

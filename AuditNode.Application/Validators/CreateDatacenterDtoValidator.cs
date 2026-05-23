using FluentValidation;
using AuditNode.Application.DTOs;

namespace AuditNode.Application.Validators;

public class CreateDatacenterDtoValidator : AbstractValidator<CreateDatacenterDto>
{
    public CreateDatacenterDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Location).NotEmpty().MaximumLength(255);
    }
}

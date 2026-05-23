using FluentValidation;
using AuditNode.Application.DTOs;

namespace AuditNode.Application.Validators;

public class CreateApplicationDtoValidator : AbstractValidator<CreateApplicationDto>
{
    public CreateApplicationDtoValidator()
    {
        RuleFor(x => x.AppCode).NotEmpty();
        RuleFor(x => x.AppName).NotEmpty();
        RuleFor(x => x.OwnerId).NotEmpty();
    }
}

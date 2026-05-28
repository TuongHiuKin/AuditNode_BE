using FluentValidation;
using AuditNode.Application.DTOs;

namespace AuditNode.Application.Validators;

public class CreateApplicationDtoValidator : AbstractValidator<CreateApplicationDto>
{
    public CreateApplicationDtoValidator()
    {
        RuleFor(x => x.AppCode).NotEmpty();
        RuleFor(x => x.AppName).NotEmpty();
        RuleFor(x => x.OwnerTeam).NotEmpty();
        RuleFor(x => x.PortNumber).InclusiveBetween(1, 65535);
        RuleFor(x => x.Protocol).NotEmpty();
        RuleFor(x => x.ServerId).NotEmpty();
    }
}

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
        RuleForEach(x => x.Labels).ChildRules(label =>
        {
            label.RuleFor(x => x.Key).NotEmpty().MaximumLength(100);
            label.RuleFor(x => x.Value).NotEmpty().MaximumLength(255);
        });
        When(x => x.Deployment is not null, () =>
        {
            RuleFor(x => x.Deployment!.ServerId).NotEmpty();
            RuleFor(x => x.Deployment!.PortNumber).InclusiveBetween(1, 65535);
            RuleFor(x => x.Deployment!.Protocol).NotEmpty().MaximumLength(20);
        });
    }
}

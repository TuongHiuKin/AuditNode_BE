using AuditNode.Application.DTOs;
using FluentValidation;

namespace AuditNode.Application.Validators;

public class UpdateServerDtoValidator : AbstractValidator<UpdateServerDto>
{
    public UpdateServerDtoValidator()
    {
        RuleFor(x => x.Hostname).NotEmpty().MaximumLength(255);
        RuleFor(x => x.IpAddress)
            .NotEmpty()
            .Must(CreateServerDtoValidator.BeValidIpv4Address)
            .WithMessage("Invalid IPv4 address format.");
        RuleFor(x => x.OsType).NotEmpty();
        RuleFor(x => x.Environment).NotEmpty();
        RuleFor(x => x.Status).NotEmpty();
        RuleFor(x => x.DatacenterId).NotEmpty();
        RuleForEach(x => x.Labels).ChildRules(label =>
        {
            label.RuleFor(x => x.Key).NotEmpty().MaximumLength(100);
            label.RuleFor(x => x.Value).NotEmpty().MaximumLength(255);
        });
    }
}

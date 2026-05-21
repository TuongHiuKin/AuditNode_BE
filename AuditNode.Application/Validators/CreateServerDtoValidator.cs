using FluentValidation;
using AuditNode.Application.DTOs;

namespace AuditNode.Application.Validators;

public class CreateServerDtoValidator : AbstractValidator<CreateServerDto>
{
    public CreateServerDtoValidator()
    {
        RuleFor(x => x.Hostname).NotEmpty().MaximumLength(255);
        RuleFor(x => x.IpAddress).NotEmpty().Matches(@"^(\d{1,3}\.){3}\d{1,3}$").WithMessage("Invalid IP Address format.");
        RuleFor(x => x.OsType).NotEmpty();
        RuleFor(x => x.Environment).NotEmpty();
        RuleFor(x => x.Status).NotEmpty();
        RuleFor(x => x.DatacenterId).NotEmpty();
    }
}

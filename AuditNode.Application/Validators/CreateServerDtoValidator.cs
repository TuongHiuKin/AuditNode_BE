using FluentValidation;
using AuditNode.Application.DTOs;

namespace AuditNode.Application.Validators;

public class CreateServerDtoValidator : AbstractValidator<CreateServerDto>
{
    public CreateServerDtoValidator()
    {
        RuleFor(x => x.Hostname).NotEmpty().MaximumLength(255);
        RuleFor(x => x.IpAddress)
            .NotEmpty()
            .Must(BeValidIpv4Address)
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

    internal static bool BeValidIpv4Address(string ipAddress)
    {
        var octets = ipAddress.Split('.');
        return octets.Length == 4 && octets.All(octet =>
            octet.Length > 0 &&
            (octet.Length == 1 || octet[0] != '0') &&
            octet.All(char.IsAsciiDigit) &&
            byte.TryParse(octet, out _));
    }
}

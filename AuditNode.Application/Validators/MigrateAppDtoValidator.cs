using AuditNode.Application.DTOs;
using FluentValidation;

namespace AuditNode.Application.Validators;

public class MigrateAppDtoValidator : AbstractValidator<MigrateAppDto>
{
    public MigrateAppDtoValidator()
    {
        RuleFor(x => x.PortMappingId).NotEmpty();
        RuleFor(x => x.TargetServerId).NotEmpty();
        RuleFor(x => x.NewPortNumber).InclusiveBetween(1, 65535);
    }
}

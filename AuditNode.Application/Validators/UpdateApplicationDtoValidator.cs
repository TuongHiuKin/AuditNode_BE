using AuditNode.Application.DTOs;
using FluentValidation;

namespace AuditNode.Application.Validators;

public class UpdateApplicationDtoValidator : AbstractValidator<UpdateApplicationDto>
{
    public UpdateApplicationDtoValidator()
    {
        RuleFor(x => x.AppName).NotEmpty();
        RuleFor(x => x.OwnerTeam).NotEmpty();
        RuleFor(x => x.Risk).NotNull();
        RuleForEach(x => x.Labels).ChildRules(label =>
        {
            label.RuleFor(x => x.Key).NotEmpty().MaximumLength(100);
            label.RuleFor(x => x.Value).NotEmpty().MaximumLength(255);
        });

        When(HasDeploymentChange, () =>
        {
            RuleFor(x => x.PortMappingId).NotNull().NotEqual(Guid.Empty);
            RuleFor(x => x.TargetServerId).NotNull().NotEqual(Guid.Empty);
            RuleFor(x => x.PortNumber).NotNull().InclusiveBetween(1, 65535);
        });
    }

    private static bool HasDeploymentChange(UpdateApplicationDto dto) =>
        dto.PortMappingId.HasValue || dto.TargetServerId.HasValue || dto.PortNumber.HasValue;
}

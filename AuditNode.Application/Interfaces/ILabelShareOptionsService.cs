using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ILabelShareOptionsService
{
    Task<LabelShareOptionsDto?> GetAsync(
        Guid labelId,
        string search,
        int first,
        int max,
        CancellationToken cancellationToken = default);
}

namespace AuditNode.Application.Interfaces;

public interface IOwnerLabelService
{
    Task EnsureAsync(string ownerUserId, CancellationToken cancellationToken = default);
}

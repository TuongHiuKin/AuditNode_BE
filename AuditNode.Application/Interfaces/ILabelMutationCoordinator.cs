namespace AuditNode.Application.Interfaces;

public interface ILabelMutationCoordinator
{
    Task<bool> ExecuteAsync(
        string ownerUserId,
        IReadOnlyCollection<Guid> requiredServerIds,
        IReadOnlyCollection<Guid> requiredApplicationIds,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken = default);
}

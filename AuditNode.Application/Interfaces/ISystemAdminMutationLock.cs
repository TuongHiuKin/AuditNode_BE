namespace AuditNode.Application.Interfaces;

public interface ISystemAdminMutationLock
{
    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}

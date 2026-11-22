using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AuditNode.Infrastructure.Services;

public sealed class LabelMutationCoordinator(
    AuditDbContext context,
    IOwnerGraphAccessService graphAccess) : ILabelMutationCoordinator
{
    public async Task<bool> ExecuteAsync(
        string ownerUserId,
        IReadOnlyCollection<Guid> requiredServerIds,
        IReadOnlyCollection<Guid> requiredApplicationIds,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return false;

        IDbContextTransaction? ownedTransaction = null;
        if (context.Database.IsRelational() && context.Database.CurrentTransaction is null)
            ownedTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await using (ownedTransaction)
        {
            var access = await graphAccess.ResolveAsync(
                ownerUserId,
                lockForWrite: context.Database.IsRelational(),
                cancellationToken);
            if (access is null ||
                requiredServerIds.Any(id => !access.EditableServerIds.Contains(id)) ||
                requiredApplicationIds.Any(id => !access.EditableApplicationIds.Contains(id)))
                return false;

            await mutation(cancellationToken);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return true;
        }
    }
}

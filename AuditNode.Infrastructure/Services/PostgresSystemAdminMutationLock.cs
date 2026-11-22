using System.Data;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditNode.Infrastructure.Services;

public sealed class PostgresSystemAdminMutationLock(AuditDbContext context) : ISystemAdminMutationLock
{
    // Stable two-key namespace for the enabled-SystemAdmin invariant. Never derive these with GetHashCode().
    private const int LockNamespace = 0x41554E44; // "AUND"
    private const int InvariantKey = 1;

    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({LockNamespace}, {InvariantKey})", cancellationToken);
            await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NpgsqlException exception)
        {
            throw new IdentityMutationLockUnavailableException(exception);
        }
    }
}

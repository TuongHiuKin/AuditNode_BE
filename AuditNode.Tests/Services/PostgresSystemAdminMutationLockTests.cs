using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class PostgresSystemAdminMutationLockTests
{
    [PostgresIntegrationFact]
    public async Task Independent_database_connections_are_mutually_exclusive()
    {
        var connectionString = ConnectionString();
        await using var firstContext = Context(connectionString);
        await using var secondContext = Context(connectionString);
        var firstLock = new PostgresSystemAdminMutationLock(firstContext);
        var secondLock = new PostgresSystemAdminMutationLock(secondContext);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = firstLock.ExecuteAsync(async _ =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
        });
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = secondLock.ExecuteAsync(_ =>
        {
            secondEntered.SetResult();
            return Task.CompletedTask;
        });

        try
        {
            await Task.Delay(250);
            secondEntered.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            releaseFirst.TrySetResult();
        }
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        secondEntered.Task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [PostgresIntegrationFact]
    public async Task Waiting_connection_honors_cancellation_without_running_mutation()
    {
        var connectionString = ConnectionString();
        await using var firstContext = Context(connectionString);
        await using var secondContext = Context(connectionString);
        var firstLock = new PostgresSystemAdminMutationLock(firstContext);
        var secondLock = new PostgresSystemAdminMutationLock(secondContext);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutationRan = false;
        var first = firstLock.ExecuteAsync(async _ =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
        });
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var action = () => secondLock.ExecuteAsync(_ =>
        {
            mutationRan = true;
            return Task.CompletedTask;
        }, cancellation.Token);

        try
        {
            await action.Should().ThrowAsync<OperationCanceledException>();
            mutationRan.Should().BeFalse();
        }
        finally
        {
            releaseFirst.TrySetResult();
        }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [PostgresIntegrationFact]
    public async Task Lock_timeout_fails_closed_without_running_mutation()
    {
        var connectionString = ConnectionString();
        await using var firstContext = Context(connectionString);
        await using var secondContext = Context(connectionString);
        var firstLock = new PostgresSystemAdminMutationLock(firstContext);
        var secondLock = new PostgresSystemAdminMutationLock(secondContext);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutationRan = false;
        var first = firstLock.ExecuteAsync(async _ =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
        });
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var action = () => secondLock.ExecuteAsync(_ =>
        {
            mutationRan = true;
            return Task.CompletedTask;
        });

        try
        {
            await action.Should().ThrowAsync<IdentityMutationLockUnavailableException>();
            mutationRan.Should().BeFalse();
        }
        finally
        {
            releaseFirst.TrySetResult();
        }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [PostgresIntegrationFact]
    public async Task Callback_failure_releases_transaction_scoped_lock()
    {
        var connectionString = ConnectionString();
        await using var firstContext = Context(connectionString);
        await using var secondContext = Context(connectionString);
        var firstLock = new PostgresSystemAdminMutationLock(firstContext);
        var secondLock = new PostgresSystemAdminMutationLock(secondContext);

        var failingAction = () => firstLock.ExecuteAsync(_ => throw new InvalidOperationException("synthetic callback failure"));
        await failingAction.Should().ThrowAsync<InvalidOperationException>();

        var secondRan = false;
        await secondLock.ExecuteAsync(_ =>
        {
            secondRan = true;
            return Task.CompletedTask;
        }).WaitAsync(TimeSpan.FromSeconds(2));
        secondRan.Should().BeTrue();
    }

    private static string ConnectionString() => Environment.GetEnvironmentVariable("AUDITNODE_TEST_POSTGRES")!;

    private static AuditDbContext Context(string connectionString)
    {
        var tenant = new Mock<ITenantProvider>();
        return new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(connectionString)
            .Options, tenant.Object);
    }
}

public sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public PostgresIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDITNODE_TEST_POSTGRES")))
            Skip = "Set AUDITNODE_TEST_POSTGRES to run PostgreSQL advisory-lock integration tests.";
    }
}

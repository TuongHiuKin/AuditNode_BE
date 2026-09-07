using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuditNode.API.IntegrationTests.Fixtures;

internal static class TestDatabase
{
    public const string EnvironmentVariable = "AUDITNODE_TEST_POSTGRES";

    public static string RequireConnectionString() =>
        Environment.GetEnvironmentVariable(EnvironmentVariable)
        ?? throw new InvalidOperationException(
            $"Set {EnvironmentVariable} to a disposable PostgreSQL database before running API integration tests.");

    public static async Task MigrateAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await database.Database.MigrateAsync();
    }
}

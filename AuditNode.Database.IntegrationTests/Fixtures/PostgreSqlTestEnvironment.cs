using System.Text.RegularExpressions;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditNode.Database.IntegrationTests.Fixtures;

public static partial class PostgreSqlTestEnvironment
{
    public const string EnvironmentVariable = "AUDITNODE_TEST_POSTGRES";
    private const string SuiteDatabasePrefix = "auditnode_suite_test_";
    private const string FreshDatabasePrefix = "auditnode_migration_test_";

    public static string RequireDisposableConnectionString()
    {
        var raw = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                $"Set {EnvironmentVariable} to an explicitly disposable PostgreSQL database.");

        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (string.IsNullOrWhiteSpace(builder.Host) ||
            string.IsNullOrWhiteSpace(builder.Database) ||
            !AllowedHost(builder.Host) ||
            !AllowedDatabaseName().IsMatch(builder.Database))
            throw new InvalidOperationException(
                $"{EnvironmentVariable} must target an allowlisted local/CI AuditNode test database.");

        return builder.ConnectionString;
    }

    public static AuditDbContext CreateContext(string? connectionString = null) => new(
        new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(connectionString ?? RequireDisposableConnectionString())
            .Options);

    public static async Task MigrateDatabaseAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
    }

    public static Task<FreshDatabase> CreateSuiteDatabaseAsync() =>
        CreateFreshDatabaseAsync(SuiteDatabasePrefix);

    public static Task<FreshDatabase> CreateMigrationDatabaseAsync() =>
        CreateFreshDatabaseAsync(FreshDatabasePrefix);

    private static async Task<FreshDatabase> CreateFreshDatabaseAsync(string prefix)
    {
        var baseConnectionString = RequireDisposableConnectionString();
        var targetDatabase = $"{prefix}{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };

        await using (var connection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {QuoteIdentifier(targetDatabase)}";
            await command.ExecuteNonQueryAsync();
        }

        var targetBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = targetDatabase };
        return new FreshDatabase(prefix, targetDatabase, targetBuilder.ConnectionString, adminBuilder.ConnectionString);
    }

    private static bool AllowedHost(string host) => host
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .All(value => value is "localhost" or "127.0.0.1" or "postgres");

    private static string QuoteIdentifier(string identifier) =>
        new NpgsqlCommandBuilder().QuoteIdentifier(identifier);

    [GeneratedRegex("^auditnode(?:_[a-z0-9]+)*(?:_test|_ci|_e2e)$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedDatabaseName();

    public sealed class FreshDatabase(
        string expectedPrefix,
        string databaseName,
        string connectionString,
        string adminConnectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public async ValueTask DisposeAsync()
        {
            if (!databaseName.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
                databaseName.Length != expectedPrefix.Length + 32)
                throw new InvalidOperationException("Refusing to remove a database outside its generated test prefix.");

            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE {QuoteIdentifier(databaseName)} WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
        }
    }
}

namespace AuditNode.Database.IntegrationTests.Fixtures;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlDatabaseFixture>
{
    public const string Name = "PostgreSQL database integration";
}

public sealed class PostgreSqlDatabaseFixture : IAsyncLifetime
{
    private PostgreSqlTestEnvironment.FreshDatabase? _database;

    public string ConnectionString => _database?.ConnectionString
        ?? throw new InvalidOperationException("PostgreSQL test database has not been initialized.");

    public async Task InitializeAsync()
    {
        _database = await PostgreSqlTestEnvironment.CreateSuiteDatabaseAsync();
        await PostgreSqlTestEnvironment.MigrateDatabaseAsync(_database.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }
}

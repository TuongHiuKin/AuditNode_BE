using Npgsql;

namespace AuditNode.Database.IntegrationTests.Fixtures;

internal sealed class PostgreSqlFailureTrigger : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly string _table;
    private readonly string _trigger;
    private readonly string _function;

    private PostgreSqlFailureTrigger(
        string connectionString,
        string table,
        string trigger,
        string function)
    {
        _connectionString = connectionString;
        _table = table;
        _trigger = trigger;
        _function = function;
    }

    public static Task<PostgreSqlFailureTrigger> ForPortMappingAsync(
        string connectionString,
        int rejectedPort) => CreateAsync(
        connectionString,
        "port_mappings",
        $"NEW.port_number = {rejectedPort}");

    public static Task<PostgreSqlFailureTrigger> ForDependencyAsync(
        string connectionString,
        Guid rejectedDestinationPortId) => CreateAsync(
        connectionString,
        "app_dependencies",
        $"NEW.dest_port_id = '{rejectedDestinationPortId:D}'::uuid");

    private static async Task<PostgreSqlFailureTrigger> CreateAsync(
        string connectionString,
        string table,
        string condition)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var trigger = $"auditnode_test_trigger_{suffix}";
        var function = $"auditnode_test_failure_{suffix}";
        var fixture = new PostgreSqlFailureTrigger(connectionString, table, trigger, function);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $$"""
            CREATE FUNCTION "{{function}}"() RETURNS trigger
            LANGUAGE plpgsql
            AS $auditnode_test$
            BEGIN
                IF {{condition}} THEN
                    RAISE EXCEPTION 'auditnode synthetic PostgreSQL failure';
                END IF;
                RETURN NEW;
            END;
            $auditnode_test$;
            CREATE TRIGGER "{{trigger}}"
            BEFORE INSERT ON "{{table}}"
            FOR EACH ROW EXECUTE FUNCTION "{{function}}"();
            """;
        await command.ExecuteNonQueryAsync();
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $$"""
            DROP TRIGGER IF EXISTS "{{_trigger}}" ON "{{_table}}";
            DROP FUNCTION IF EXISTS "{{_function}}"();
            """;
        await command.ExecuteNonQueryAsync();
    }
}

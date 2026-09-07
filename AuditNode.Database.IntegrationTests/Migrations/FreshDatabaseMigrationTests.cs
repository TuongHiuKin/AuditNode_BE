using AuditNode.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditNode.Database.IntegrationTests.Migrations;

[Trait("Category", "PostgreSQL")]
[Trait("Suite", "MigrationFresh")]
[Collection(PostgreSqlCollection.Name)]
public sealed class FreshDatabaseMigrationTests
{
    private static readonly string[] RequiredRelations =
    [
        "datacenters", "servers", "applications", "port_mappings", "app_dependencies",
        "topology_nodes", "topology_edges", "labels", "server_labels", "application_labels",
        "label_grants", "owner_catalog_states", "\"__EFMigrationsHistory\""
    ];

    [Fact]
    public async Task Empty_database_migrates_to_complete_head_and_second_run_is_idempotent()
    {
        await using var database = await PostgreSqlTestEnvironment.CreateMigrationDatabaseAsync();
        await using var context = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);

        await context.Database.MigrateAsync();

        var expectedMigrations = context.Database.GetMigrations().ToArray();
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        appliedMigrations.Should().Equal(expectedMigrations);
        foreach (var relation in RequiredRelations)
            (await RelationExistsAsync(database.ConnectionString, relation)).Should().BeTrue();
        foreach (var legacy in new[] { "workspaces", "workspace_members", "workspace_member_scopes" })
            (await RelationExistsAsync(database.ConnectionString, legacy)).Should().BeFalse();
        await ViewCanBeQueriedAsync(database.ConnectionString, "v_topology_map");
        await ViewCanBeQueriedAsync(database.ConnectionString, "v_dependency_graph");
        (await context.Database.CanConnectAsync()).Should().BeTrue();

        await context.Database.MigrateAsync();

        (await context.Database.GetAppliedMigrationsAsync()).Should().Equal(expectedMigrations);
    }

    private static async Task<bool> RelationExistsAsync(string connectionString, string relation)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass(@relation) IS NOT NULL";
        command.Parameters.AddWithValue("relation", $"public.{relation}");
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ViewCanBeQueriedAsync(string connectionString, string view)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM \"{view}\" LIMIT 0";
        await command.ExecuteNonQueryAsync();
    }
}

using AuditNode.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditNode.Database.IntegrationTests.Migrations;

[Trait("Category", "PostgreSQL")]
[Trait("Suite", "MigrationCutover")]
[Collection(PostgreSqlCollection.Name)]
public sealed class LegacyCatalogCutoverMigrationTests
{
    private const string PreCutoverMigration =
        "20260830090508_GlobalCatalogTransitionalLabelUniqueness";

    [Fact]
    public async Task Empty_pre_cutover_schema_migrates_to_head_and_is_idempotent()
    {
        await using var database = await PostgreSqlTestEnvironment.CreateMigrationDatabaseAsync();
        await using var context = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(PreCutoverMigration);
        (await RelationExistsAsync(database.ConnectionString, "workspaces")).Should().BeTrue();

        await migrator.MigrateAsync();

        var expectedMigrations = context.Database.GetMigrations().ToArray();
        (await context.Database.GetAppliedMigrationsAsync()).Should().Equal(expectedMigrations);
        foreach (var legacy in new[]
                 {
                     "workspaces",
                     "workspace_members",
                     "workspace_member_scopes",
                     "workspace_member_rbac_provenance",
                     "workspace_member_scope_backfill_audit"
                 })
            (await RelationExistsAsync(database.ConnectionString, legacy)).Should().BeFalse();

        await ViewCanBeQueriedAsync(database.ConnectionString, "v_topology_map");
        await ViewCanBeQueriedAsync(database.ConnectionString, "v_dependency_graph");

        await migrator.MigrateAsync();

        (await context.Database.GetAppliedMigrationsAsync()).Should().Equal(expectedMigrations);
    }

    [Fact]
    public async Task Populated_pre_cutover_schema_fails_closed_without_changing_data_or_history()
    {
        await using var database = await PostgreSqlTestEnvironment.CreateMigrationDatabaseAsync();
        await using var context = PostgreSqlTestEnvironment.CreateContext(database.ConnectionString);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(PreCutoverMigration);
        await InsertLegacyCatalogStateAsync(database.ConnectionString);
        var topologyViewExisted = await RelationExistsAsync(database.ConnectionString, "v_topology_map");
        var dependencyViewExisted = await RelationExistsAsync(database.ConnectionString, "v_dependency_graph");

        var action = () => migrator.MigrateAsync();

        var exception = await action.Should().ThrowAsync<PostgresException>();
        exception.Which.MessageText.Should()
            .Contain("RemoveWorkspaceRuntime requires the approved Phase 8 reset");
        (await context.Database.GetAppliedMigrationsAsync()).Should().EndWith(PreCutoverMigration);
        (await CountOwnerCatalogStatesAsync(database.ConnectionString)).Should().Be(1);
        (await RelationExistsAsync(database.ConnectionString, "workspaces")).Should().BeTrue();
        (await RelationExistsAsync(database.ConnectionString, "v_topology_map"))
            .Should().Be(topologyViewExisted);
        (await RelationExistsAsync(database.ConnectionString, "v_dependency_graph"))
            .Should().Be(dependencyViewExisted);
    }

    private static async Task InsertLegacyCatalogStateAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO owner_catalog_states (owner_user_id, topology_version, updated_at)
            VALUES ('phase2-cutover-owner', 7, TIMESTAMPTZ '2026-09-08 00:00:00+00')
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountOwnerCatalogStatesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM owner_catalog_states";
        return (long)(await command.ExecuteScalarAsync())!;
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

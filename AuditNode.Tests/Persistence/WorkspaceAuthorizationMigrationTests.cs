using AuditNode.Infrastructure.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;
using Xunit;

namespace AuditNode.Tests.Persistence;

public class WorkspaceAuthorizationMigrationTests
{
    [Fact]
    public void Up_Should_Stage_Preexisting_Owner_And_Datacenter_Backfill_As_Nullable()
    {
        var operations = new InspectableMigration().GetUpOperations();

        operations.OfType<AddColumnOperation>()
            .Single(operation => operation.Table == "workspaces" && operation.Name == "owner_user_id")
            .IsNullable.Should().BeTrue();
        operations.OfType<AddColumnOperation>()
            .Single(operation => operation.Table == "datacenters" && operation.Name == "workspace_id")
            .IsNullable.Should().BeTrue();

        operations.OfType<AlterColumnOperation>().Should().NotContain(operation =>
            (operation.Table == "workspaces" && operation.Name == "owner_user_id") ||
            (operation.Table == "datacenters" && operation.Name == "workspace_id"));
    }

    [Fact]
    public void Up_Should_Not_Infer_Owners_From_Legacy_Optional_Columns_Or_Abort_Unassigned_Rows()
    {
        var sql = string.Join('\n', new InspectableMigration().GetUpOperations()
            .OfType<SqlOperation>()
            .Select(operation => operation.Sql));

        sql.Should().NotContain("owner_id");
        sql.Should().NotContain("RAISE EXCEPTION");
        sql.Should().Contain("count(DISTINCT server.workspace_id) = 1");
    }

    [Fact]
    public void Finalize_Migration_Should_Gate_NotNull_Constraints_Without_Backfilling_Data()
    {
        var migrationType = typeof(WorkspaceAuthorizationConsistency).Assembly.GetTypes()
            .SingleOrDefault(type => type.Name == "FinalizeWorkspaceOwnershipConstraints");
        migrationType.Should().NotBeNull("staged nullable columns require a separate operator-gated finalization migration");

        var migration = (Migration)Activator.CreateInstance(migrationType!)!;
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        migrationType!.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var sql = string.Join('\n', builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        sql.Should().Contain("owner_user_id IS NULL");
        sql.Should().Contain("workspace_id IS NULL");
        sql.Should().Contain("RAISE EXCEPTION");
        sql.Should().NotContain("UPDATE workspaces");
        sql.Should().NotContain("UPDATE datacenters");
        sql.Should().Contain("ALTER TABLE workspaces ALTER COLUMN owner_user_id SET NOT NULL");
        sql.Should().Contain("ALTER TABLE datacenters ALTER COLUMN workspace_id SET NOT NULL");
        sql.IndexOf("ALTER TABLE workspaces", StringComparison.Ordinal)
            .Should().BeGreaterThan(sql.IndexOf("RAISE EXCEPTION", StringComparison.Ordinal));
        Assert.DoesNotContain(builder.Operations, operation => operation is UpdateDataOperation or DeleteDataOperation);
    }

    [Fact]
    public void ServerLabel_Migration_Should_Backfill_Only_Matching_Workspaces_Before_Constraints()
    {
        var operations = new InspectableServerLabelMigration().GetUpOperations();
        var workspaceColumn = operations.OfType<AddColumnOperation>()
            .Single(operation => operation.Table == "server_labels" && operation.Name == "workspace_id");
        var sqlIndex = operations.ToList().FindIndex(operation => operation is SqlOperation);
        var requiredIndex = operations.ToList().FindIndex(operation => operation is AlterColumnOperation alter &&
            alter.Table == "server_labels" && alter.Name == "workspace_id" && !alter.IsNullable);
        var sql = string.Join('\n', operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        workspaceColumn.IsNullable.Should().BeTrue();
        operations.OfType<RenameColumnOperation>().Should().Contain(operation =>
            operation.Table == "server_labels" && operation.Name == "ServersId" && operation.NewName == "server_id");
        operations.OfType<RenameColumnOperation>().Should().Contain(operation =>
            operation.Table == "server_labels" && operation.Name == "LabelsId" && operation.NewName == "label_id");
        sql.Should().Contain("server.workspace_id <> label.workspace_id");
        sql.Should().Contain("RAISE EXCEPTION");
        sql.Should().Contain("UPDATE server_labels");
        requiredIndex.Should().BeGreaterThan(sqlIndex);
        Assert.DoesNotContain(operations, operation => operation is DropTableOperation or DeleteDataOperation);
    }

    [Fact]
    public void Topology_Migration_Should_Convert_Only_Unambiguous_Legacy_Application_References()
    {
        var operations = new InspectableTopologyMigration().GetUpOperations();
        var sql = string.Join('\n', operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        sql.Should().Contain("lower(node.node_type) = 'application'");
        sql.Should().Contain("port_mappings");
        sql.Should().Contain("count(mapping.id) > 1");
        sql.Should().Contain("count(mapping.id) = 1");
        sql.Should().Contain("ambiguous legacy application reference");
        sql.Should().Contain("unresolvable legacy application reference");
        sql.Should().Contain("UPDATE topology_nodes");
        sql.Should().NotContain("DELETE FROM topology_nodes");
    }

    private sealed class InspectableMigration : WorkspaceAuthorizationConsistency
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }
    }


    private sealed class InspectableServerLabelMigration : TenantScopedServerLabels
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }
    }

    private sealed class InspectableTopologyMigration : TopologyCanonicalState
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }
    }
}

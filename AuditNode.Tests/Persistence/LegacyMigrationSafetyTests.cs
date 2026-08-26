using System.Reflection;
using AuditNode.Infrastructure.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace AuditNode.Tests.Persistence;

public sealed class LegacyMigrationSafetyTests
{
    [Fact]
    public void Initial_Baseline_Should_Create_Empty_Schema_And_NoOp_Complete_Installations()
    {
        var operations = new InspectableInitialMigration().GetUpOperations();
        var sql = operations.OfType<SqlOperation>().Single().Sql;

        sql.Should().Contain("IF existing_count = 7");
        sql.Should().Contain("ELSIF existing_count <> 0");
        sql.Should().Contain("Partial legacy schema detected");
        sql.Should().Contain("CREATE TABLE servers");
        sql.Should().Contain("CREATE TABLE auditnode_schema_baseline_provenance");
        sql.Should().NotContain("CREATE TABLE labels");
    }

    [Fact]
    public void AddLabels_Should_Refuse_Untracked_Existing_Label_Tables()
    {
        var operations = new InspectableAddLabelsMigration().GetUpOperations();
        var sql = operations.OfType<SqlOperation>().First().Sql;

        sql.Should().Contain("Untracked labels schema detected");
        sql.Should().NotContain("DROP TABLE");
    }

    [Fact]
    public void Preflight_Should_Be_ReadOnly_And_Fail_Closed()
    {
        var sql = ReadEmbeddedSql("20260826_rbac_scope_preflight.sql");

        sql.Should().Contain("BEGIN TRANSACTION READ ONLY");
        sql.Should().Contain("ARRAY[]::uuid[]");
        sql.Should().Contain("member.role <> 'workspace_admin'");
        sql.Should().Contain("WHEN member.role = 'editor' THEN 'auditor'");
        sql.Should().Contain("\\if :rbac_schema_exists");
        sql.Should().Contain("\\if :workspace_members_exists");
        sql.Should().NotContain("CREATE TEMP TABLE");
        sql.Should().NotContain("UPDATE workspace_members");
    }

    [Fact]
    public void Backfill_Should_Validate_Workspace_Targets_And_Be_Idempotent()
    {
        var sql = ReadEmbeddedSql("20260826_rbac_scope_backfill.sql");

        sql.Should().Contain("label.workspace_id = mapping.workspace_id");
        sql.Should().Contain("frame.workspace_id = mapping.workspace_id");
        sql.Should().Contain("Duplicate scope target in mapping");
        sql.Should().Contain("Approved mapping is empty");
        sql.Should().Contain("multiple approvers");
        sql.Should().Contain("LOCK TABLE workspace_members");
        sql.Should().Contain("labels,");
        sql.Should().Contain("topology_nodes,");
        sql.Should().Contain("workspace_member_rbac_provenance");
        sql.Should().Contain("FROM pstdin");
        sql.Should().Contain("has_wrong_scope_type");
        sql.Should().Contain("workspace_member_scope_backfill_audit");
        sql.Should().Contain("without legacy role provenance");
        sql.Should().Contain("changed_rbac_members");
        sql.Should().Contain("ON CONFLICT (workspace_id, user_id, scope_type, target_id) DO NOTHING");
        sql.Should().Contain("BEGIN;");
        sql.Should().Contain("COMMIT;");
    }

    [Fact]
    public void WorkspaceConsistency_Should_Keep_Unresolved_Datacenter_Workspace_Null()
    {
        var operations = new InspectableWorkspaceConsistencyMigration().GetUpOperations();
        var sql = string.Join('\n', operations.OfType<SqlOperation>().Select(x => x.Sql));

        sql.Should().Contain("HAVING count(DISTINCT server.workspace_id) = 1");
        sql.Should().NotContain("00000000-0000-0000-0000-000000000000");
    }

    private static string ReadEmbeddedSql(string suffix)
    {
        var assembly = typeof(InitialLegacySchema).Assembly;
        var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class InspectableInitialMigration : InitialLegacySchema
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }
    }

    private sealed class InspectableAddLabelsMigration : AddLabels
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }
    }

    private sealed class InspectableWorkspaceConsistencyMigration : WorkspaceAuthorizationConsistency
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }
    }
}

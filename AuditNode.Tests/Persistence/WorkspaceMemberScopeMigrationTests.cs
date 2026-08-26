using AuditNode.Infrastructure.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace AuditNode.Tests.Persistence;

public class WorkspaceMemberScopeMigrationTests
{
    [Fact]
    public void SafetySupport_Should_Capture_Roles_And_Persist_Approval_Evidence()
    {
        var operations = new InspectableSupportMigration().GetUpOperations();
        var sql = string.Join('\n', operations.OfType<SqlOperation>().Select(x => x.Sql));

        operations.OfType<CreateTableOperation>().Select(x => x.Name).Should().Contain([
            "workspace_member_rbac_provenance",
            "workspace_member_scope_backfill_audit"]);
        operations.OfType<AlterColumnOperation>()
            .Single(x => x.Table == "workspaces" && x.Name == "description")
            .IsNullable.Should().BeTrue();
        sql.Should().Contain("20260823161807_RbacScopedSharing");
        sql.Should().Contain("role = 'auditor' THEN NULL");
        sql.Should().Contain("requires_manual_decision");
        sql.Should().Contain("'pre_rbac'");
    }

    [Fact]
    public void RbacUp_Should_BackfillEditorAndCreateScopedMembershipSchema()
    {
        var migration = new InspectableMigration();
        var operations = migration.GetUpOperations();
        var operationList = operations.ToList();
        var provenanceCaptureIndex = operationList.FindIndex(x => x is SqlOperation sql
            && sql.Sql.Contains("'rbac_apply'") && sql.Sql.Contains("role = 'editor'"));
        var editorUpdateIndex = operationList.FindIndex(x => x is SqlOperation sql
            && sql.Sql.Contains("SET role = 'auditor'") && sql.Sql.Contains("role = 'editor'"));

        provenanceCaptureIndex.Should().BeGreaterThanOrEqualTo(0);
        editorUpdateIndex.Should().BeGreaterThan(provenanceCaptureIndex);
        operations.OfType<SqlOperation>().Single(x => x.Sql.Contains("'rbac_apply'"))
            .Sql.Should().Contain("ON CONFLICT (workspace_id, user_id) DO UPDATE")
            .And.Contain("review_artifact_sha256 = NULL");
        operations.OfType<SqlOperation>().Should().Contain(x => x.Sql.Contains("role = 'auditor'") && x.Sql.Contains("role = 'editor'"));
        operations.OfType<CreateTableOperation>().Should().Contain(x => x.Name == "workspace_member_scopes");
        operations.OfType<CreateTableOperation>().Should().NotContain(x => x.Name == "workspace_member_rbac_provenance");
        operations.OfType<AddColumnOperation>().Single(x => x.Table == "workspace_members" && x.Name == "scope_mode")
            .DefaultValue.Should().Be("labels", "legacy Viewer/Auditor rows must fail closed until scopes are explicitly granted");
        operations.OfType<SqlOperation>().Should().Contain(x => x.Sql.Contains("scope_mode = 'all'") && x.Sql.Contains("workspace_admin"));
        operations.OfType<CreateIndexOperation>().Should().Contain(x => x.Table == "workspace_member_scopes" && x.IsUnique);
        operations.OfType<AlterColumnOperation>().Should().NotContain(x => x.Table == "workspaces" && x.Name == "description");
        operations.OfType<AddCheckConstraintOperation>().Select(x => x.Name).Should().Contain([
            "ck_workspace_members_scope_mode", "ck_workspace_members_admin_all"]);
        Assert.DoesNotContain(operations, x => x is DropTableOperation or DeleteDataOperation);
    }

    [Fact]
    public void Down_Should_Restore_Only_Provenance_Editors()
    {
        var operations = new InspectableMigration().GetDownOperations();
        var sql = string.Join('\n', operations.OfType<SqlOperation>().Select(x => x.Sql));

        sql.Should().Contain("workspace_member_rbac_provenance");
        sql.Should().Contain("RBAC rollback blocked");
        sql.Should().Contain("requires_manual_decision OR original_role IS NULL");
        sql.Should().Contain("provenance.original_role = 'editor'");
        sql.Should().Contain("member.role = 'auditor'");
        sql.Should().NotContain("SET role = 'editor' WHERE role = 'auditor'");
        operations.OfType<DropTableOperation>().Should().NotContain(x => x.Name == "workspace_member_rbac_provenance");
        operations.ToList().FindIndex(x => x is DropCheckConstraintOperation check && check.Name == "ck_workspace_members_role")
            .Should().BeLessThan(operations.ToList().FindIndex(x => x is SqlOperation operation && operation.Sql.Contains("original_role")));
    }

    private sealed class InspectableSupportMigration : RbacMigrationSafetySupport
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }
    }

    private sealed class InspectableMigration : RbacScopedSharing
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }


        public IReadOnlyList<MigrationOperation> GetDownOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Down(builder);
            return builder.Operations;
        }
    }
}

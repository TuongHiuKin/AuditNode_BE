using AuditNode.Infrastructure.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace AuditNode.Tests.Persistence;

public class WorkspaceMemberScopeMigrationTests
{
    [Fact]
    public void Up_Should_BackfillEditorAndCreateScopedMembershipSchema()
    {
        var migration = new InspectableMigration();
        var operations = migration.GetUpOperations();
        operations.OfType<SqlOperation>().Should().Contain(x => x.Sql.Contains("role = 'auditor'") && x.Sql.Contains("role = 'editor'"));
        operations.OfType<CreateTableOperation>().Should().Contain(x => x.Name == "workspace_member_scopes");
        operations.OfType<AddColumnOperation>().Single(x => x.Table == "workspace_members" && x.Name == "scope_mode")
            .DefaultValue.Should().Be("labels", "legacy Viewer/Auditor rows must fail closed until scopes are explicitly granted");
        operations.OfType<SqlOperation>().Should().Contain(x => x.Sql.Contains("scope_mode = 'all'") && x.Sql.Contains("workspace_admin"));
        operations.OfType<CreateIndexOperation>().Should().Contain(x => x.Table == "workspace_member_scopes" && x.IsUnique);
        operations.OfType<AlterColumnOperation>().Should().NotContain(x => x.Table == "workspaces" && x.Name == "description");
        operations.OfType<AddCheckConstraintOperation>().Select(x => x.Name).Should().Contain([
            "ck_workspace_members_scope_mode", "ck_workspace_members_admin_all"]);
        Assert.DoesNotContain(operations, x => x is DropTableOperation or DeleteDataOperation);
    }

    private sealed class InspectableMigration : RbacScopedSharing
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }
    }
}

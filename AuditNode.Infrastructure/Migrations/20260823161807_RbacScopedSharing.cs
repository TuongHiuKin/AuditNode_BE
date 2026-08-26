using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RbacScopedSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workspace_members_role",
                table: "workspace_members");

            migrationBuilder.Sql(
                """
                INSERT INTO workspace_member_rbac_provenance
                    (workspace_id, user_id, original_role, captured_role, capture_source, requires_manual_decision)
                SELECT workspace_id, user_id, role, role, 'rbac_apply', false
                FROM workspace_members
                WHERE role = 'editor'
                ON CONFLICT (workspace_id, user_id) DO UPDATE
                SET original_role = 'editor',
                    captured_role = 'editor',
                    capture_source = 'rbac_apply',
                    requires_manual_decision = false,
                    reviewed_by_user_id = NULL,
                    reviewed_at = NULL,
                    review_artifact = NULL,
                    review_artifact_sha256 = NULL,
                    captured_at = CURRENT_TIMESTAMP;
                """);

            migrationBuilder.Sql("UPDATE workspace_members SET role = 'auditor' WHERE role = 'editor';");

            migrationBuilder.AddColumn<string>(
                name: "scope_mode",
                table: "workspace_members",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "labels");

            migrationBuilder.Sql("UPDATE workspace_members SET scope_mode = 'all' WHERE role = 'workspace_admin';");

            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "workspace_members",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "workspace_member_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_member_scopes", x => x.id);
                    table.CheckConstraint("ck_workspace_member_scopes_type", "scope_type IN ('label', 'frame')");
                    table.CheckConstraint("ck_workspace_member_scopes_target", "target_id <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_workspace_member_scopes_workspace_members_workspace_id_user~",
                        columns: x => new { x.workspace_id, x.user_id },
                        principalTable: "workspace_members",
                        principalColumns: new[] { "workspace_id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_workspace_members_role",
                table: "workspace_members",
                sql: "role IN ('workspace_admin', 'auditor', 'viewer')");

            migrationBuilder.AddCheckConstraint(name: "ck_workspace_members_scope_mode", table: "workspace_members", sql: "scope_mode IN ('all', 'labels', 'frames')");
            migrationBuilder.AddCheckConstraint(name: "ck_workspace_members_admin_all", table: "workspace_members", sql: "role <> 'workspace_admin' OR scope_mode = 'all'");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_member_scopes_workspace_id_user_id",
                table: "workspace_member_scopes",
                columns: new[] { "workspace_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_member_scopes_workspace_id_user_id_scope_type_tar~",
                table: "workspace_member_scopes",
                columns: new[] { "workspace_id", "user_id", "scope_type", "target_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workspace_members_role",
                table: "workspace_members");
            migrationBuilder.DropCheckConstraint(name: "ck_workspace_members_scope_mode", table: "workspace_members");
            migrationBuilder.DropCheckConstraint(name: "ck_workspace_members_admin_all", table: "workspace_members");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM workspace_member_rbac_provenance
                        WHERE requires_manual_decision OR original_role IS NULL
                    ) THEN
                        RAISE EXCEPTION 'RBAC rollback blocked: unresolved legacy auditor provenance exists. Complete the RBAC migration runbook review first.';
                    END IF;
                END $$;

                UPDATE workspace_members AS member
                SET role = 'editor'
                FROM workspace_member_rbac_provenance AS provenance
                WHERE member.workspace_id = provenance.workspace_id
                  AND member.user_id = provenance.user_id
                  AND member.role = 'auditor'
                  AND provenance.original_role = 'editor';
                """);

            migrationBuilder.DropTable(
                name: "workspace_member_scopes");

            migrationBuilder.DropColumn(
                name: "scope_mode",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "version",
                table: "workspace_members");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workspace_members_role",
                table: "workspace_members",
                sql: "role IN ('workspace_admin', 'editor', 'auditor', 'viewer')");
        }
    }
}

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
            migrationBuilder.Sql("UPDATE workspace_members SET role = 'editor' WHERE role = 'auditor';");
            migrationBuilder.DropTable(
                name: "workspace_member_scopes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workspace_members_role",
                table: "workspace_members");
            migrationBuilder.DropCheckConstraint(name: "ck_workspace_members_scope_mode", table: "workspace_members");
            migrationBuilder.DropCheckConstraint(name: "ck_workspace_members_admin_all", table: "workspace_members");

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

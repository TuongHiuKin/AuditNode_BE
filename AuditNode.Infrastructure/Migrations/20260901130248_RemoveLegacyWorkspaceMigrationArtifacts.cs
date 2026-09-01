using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyWorkspaceMigrationArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_member_scope_backfill_audit");

            migrationBuilder.DropTable(
                name: "workspace_member_rbac_provenance");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workspace_member_rbac_provenance",
                columns: table => new
                {
                    workspace_id = table.Column<System.Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    captured_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    capture_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requires_manual_decision = table.Column<bool>(type: "boolean", nullable: false),
                    reviewed_by_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reviewed_at = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: true),
                    review_artifact = table.Column<string>(type: "text", nullable: true),
                    review_artifact_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    captured_at = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_member_rbac_provenance", x => new { x.workspace_id, x.user_id });
                    table.CheckConstraint(
                        "ck_workspace_member_rbac_provenance_original_role",
                        "original_role IS NULL OR original_role IN ('workspace_admin', 'editor', 'auditor', 'viewer')");
                });

            migrationBuilder.CreateTable(
                name: "workspace_member_scope_backfill_audit",
                columns: table => new
                {
                    workspace_id = table.Column<System.Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    approved_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    approved_scope_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approved_target_ids = table.Column<System.Guid[]>(type: "uuid[]", nullable: false),
                    approved_by_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    approved_at = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_member_scope_backfill_audit", x => new { x.workspace_id, x.user_id });
                    table.CheckConstraint(
                        "ck_workspace_member_scope_backfill_audit_mode",
                        "approved_scope_mode IN ('all', 'labels', 'frames')");
                });
        }
    }
}

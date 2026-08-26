using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations;

/// <summary>
/// Adds the durable evidence required to migrate and roll back legacy workspace roles safely.
/// The migration is deliberately ordered immediately before RbacScopedSharing. On databases
/// where RbacScopedSharing was already applied, legacy auditor provenance is unknowable and is
/// therefore marked for an explicit operator decision rather than guessed.
/// </summary>
[DbContext(typeof(AuditDbContext))]
[Migration("20260823160000_RbacMigrationSafetySupport")]
public class RbacMigrationSafetySupport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "description",
            table: "workspaces",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.CreateTable(
            name: "workspace_member_rbac_provenance",
            columns: table => new
            {
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                original_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                captured_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                capture_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                requires_manual_decision = table.Column<bool>(type: "boolean", nullable: false),
                reviewed_by_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                review_artifact = table.Column<string>(type: "text", nullable: true),
                review_artifact_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                approved_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                approved_scope_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                approved_target_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                approved_by_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workspace_member_scope_backfill_audit", x => new { x.workspace_id, x.user_id });
                table.CheckConstraint(
                    "ck_workspace_member_scope_backfill_audit_mode",
                    "approved_scope_mode IN ('all', 'labels', 'frames')");
            });

        migrationBuilder.Sql(
            """
            DO $$
            DECLARE
                rbac_already_applied boolean;
            BEGIN
                SELECT EXISTS (
                    SELECT 1
                    FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '20260823161807_RbacScopedSharing'
                ) INTO rbac_already_applied;

                IF rbac_already_applied THEN
                    INSERT INTO workspace_member_rbac_provenance
                        (workspace_id, user_id, original_role, captured_role, capture_source, requires_manual_decision)
                    SELECT
                        workspace_id,
                        user_id,
                        CASE WHEN role = 'auditor' THEN NULL ELSE role END,
                        role,
                        'retroactive',
                        role = 'auditor'
                    FROM workspace_members
                    ON CONFLICT (workspace_id, user_id) DO NOTHING;
                ELSE
                    INSERT INTO workspace_member_rbac_provenance
                        (workspace_id, user_id, original_role, captured_role, capture_source, requires_manual_decision)
                    SELECT workspace_id, user_id, role, role, 'pre_rbac', false
                    FROM workspace_members
                    ON CONFLICT (workspace_id, user_id) DO NOTHING;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "workspace_member_scope_backfill_audit");
        migrationBuilder.DropTable(name: "workspace_member_rbac_provenance");

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM workspaces WHERE description IS NULL) THEN
                    RAISE EXCEPTION 'RBAC safety-support rollback blocked: NULL workspace descriptions exist.';
                END IF;
            END $$;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "description",
            table: "workspaces",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalCatalogTransitionalLabelUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_labels_owner_user_id_key_value",
                table: "labels");

            migrationBuilder.CreateIndex(
                name: "IX_labels_workspace_id_owner_user_id_key_value",
                table: "labels",
                columns: new[] { "workspace_id", "owner_user_id", "key", "value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This rollback is conditionally reversible. Up intentionally permits the same
            // owner/key/value in different workspaces, so fail before changing indexes when
            // live data can no longer satisfy the former owner-only uniqueness contract.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM labels
                        WHERE owner_user_id IS NOT NULL
                        GROUP BY owner_user_id, key, value
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot roll back GlobalCatalogTransitionalLabelUniqueness: duplicate owner/key/value labels exist across workspaces.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_labels_workspace_id_owner_user_id_key_value",
                table: "labels");

            migrationBuilder.CreateIndex(
                name: "IX_labels_owner_user_id_key_value",
                table: "labels",
                columns: new[] { "owner_user_id", "key", "value" },
                unique: true);
        }
    }
}

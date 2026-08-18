using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeWorkspaceOwnershipConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM workspaces
                        WHERE owner_user_id IS NULL OR btrim(owner_user_id) = ''
                    ) THEN
                        RAISE EXCEPTION 'Workspace ownership backfill is incomplete; owner_user_id must contain a real user identifier.';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM datacenters
                        WHERE workspace_id IS NULL
                           OR workspace_id = '00000000-0000-0000-0000-000000000000'::uuid
                    ) THEN
                        RAISE EXCEPTION 'Datacenter workspace backfill is incomplete; workspace_id must be assigned explicitly.';
                    END IF;
                END $$;

                ALTER TABLE workspaces ALTER COLUMN owner_user_id SET NOT NULL;
                ALTER TABLE datacenters ALTER COLUMN workspace_id SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE datacenters ALTER COLUMN workspace_id DROP NOT NULL;
                ALTER TABLE workspaces ALTER COLUMN owner_user_id DROP NOT NULL;
                """);
        }
    }
}

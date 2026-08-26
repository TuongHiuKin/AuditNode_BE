using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceAuthorizationConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"app_dependencies\" DROP CONSTRAINT IF EXISTS \"FK_app_dependencies_applications_dest_app_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"app_dependencies\" DROP CONSTRAINT IF EXISTS \"FK_app_dependencies_applications_source_app_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"app_dependencies\" DROP CONSTRAINT IF EXISTS \"FK_app_dependencies_port_mappings_dest_port_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"port_mappings\" DROP CONSTRAINT IF EXISTS \"FK_port_mappings_applications_app_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"port_mappings\" DROP CONSTRAINT IF EXISTS \"FK_port_mappings_servers_server_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"servers\" DROP CONSTRAINT IF EXISTS \"FK_servers_datacenters_datacenter_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"topology_nodes\" DROP CONSTRAINT IF EXISTS \"FK_topology_nodes_topology_nodes_parent_node_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_topology_nodes_parent_node_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_servers_datacenter_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_servers_ip_address\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_port_mappings_app_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_port_mappings_server_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_applications_app_code\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_app_dependencies_dest_app_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_app_dependencies_dest_port_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_app_dependencies_source_app_id\";");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "workspaces",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "workspaces",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<bool>(
                name: "is_personal",
                table: "workspaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "workspaces",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "workspaces",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "datacenters",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                -- Stage only deterministic tenant data. Existing schemas did not retain a
                -- trustworthy user identifier from which workspace ownership can be inferred,
                -- so owner_user_id intentionally remains NULL until an operator backfills it.
                UPDATE datacenters AS datacenter
                SET workspace_id = candidate.workspace_id
                FROM (
                    SELECT server.datacenter_id,
                           min(server.workspace_id::text)::uuid AS workspace_id
                    FROM servers AS server
                    WHERE server.workspace_id IS NOT NULL
                    GROUP BY server.datacenter_id
                    HAVING count(DISTINCT server.workspace_id) = 1
                ) AS candidate
                WHERE datacenter.workspace_id IS NULL
                  AND candidate.datacenter_id = datacenter.id;
                  
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_topology_nodes_workspace_id_id",
                table: "topology_nodes",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_servers_workspace_id_id",
                table: "servers",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_port_mappings_workspace_id_id",
                table: "port_mappings",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_datacenters_workspace_id_id",
                table: "datacenters",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_applications_workspace_id_id",
                table: "applications",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.CreateTable(
                name: "workspace_members",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    invited_by_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_members", x => new { x.workspace_id, x.user_id });
                    table.CheckConstraint("ck_workspace_members_role", "role IN ('workspace_admin', 'editor', 'auditor', 'viewer')");
                    table.ForeignKey(
                        name: "FK_workspace_members_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_owner_user_id",
                table: "workspaces",
                column: "owner_user_id",
                unique: true,
                filter: "is_personal = true");

            migrationBuilder.CreateIndex(
                name: "IX_topology_nodes_workspace_id",
                table: "topology_nodes",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_topology_nodes_workspace_id_parent_node_id",
                table: "topology_nodes",
                columns: new[] { "workspace_id", "parent_node_id" });

            migrationBuilder.CreateIndex(
                name: "IX_servers_workspace_id_datacenter_id",
                table: "servers",
                columns: new[] { "workspace_id", "datacenter_id" });

            migrationBuilder.CreateIndex(
                name: "IX_servers_workspace_id_ip_address",
                table: "servers",
                columns: new[] { "workspace_id", "ip_address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_workspace_id_app_id",
                table: "port_mappings",
                columns: new[] { "workspace_id", "app_id" });

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_workspace_id_server_id_port_number",
                table: "port_mappings",
                columns: new[] { "workspace_id", "server_id", "port_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_labels_workspace_id_key_value",
                table: "labels",
                columns: new[] { "workspace_id", "key", "value" });

            migrationBuilder.CreateIndex(
                name: "IX_datacenters_workspace_id",
                table: "datacenters",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_workspace_id_app_code",
                table: "applications",
                columns: new[] { "workspace_id", "app_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_workspace_id",
                table: "app_dependencies",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_workspace_id_dest_app_id",
                table: "app_dependencies",
                columns: new[] { "workspace_id", "dest_app_id" });

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_workspace_id_dest_port_id",
                table: "app_dependencies",
                columns: new[] { "workspace_id", "dest_port_id" });

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_workspace_id_source_app_id",
                table: "app_dependencies",
                columns: new[] { "workspace_id", "source_app_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_members_user_id",
                table: "workspace_members",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_applications_workspace_id_dest_app_id",
                table: "app_dependencies",
                columns: new[] { "workspace_id", "dest_app_id" },
                principalTable: "applications",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_applications_workspace_id_source_app_id",
                table: "app_dependencies",
                columns: new[] { "workspace_id", "source_app_id" },
                principalTable: "applications",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_port_mappings_workspace_id_dest_port_id",
                table: "app_dependencies",
                columns: new[] { "workspace_id", "dest_port_id" },
                principalTable: "port_mappings",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_workspaces_workspace_id",
                table: "app_dependencies",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_applications_workspaces_workspace_id",
                table: "applications",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_datacenters_workspaces_workspace_id",
                table: "datacenters",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_labels_workspaces_workspace_id",
                table: "labels",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_port_mappings_applications_workspace_id_app_id",
                table: "port_mappings",
                columns: new[] { "workspace_id", "app_id" },
                principalTable: "applications",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_port_mappings_servers_workspace_id_server_id",
                table: "port_mappings",
                columns: new[] { "workspace_id", "server_id" },
                principalTable: "servers",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_port_mappings_workspaces_workspace_id",
                table: "port_mappings",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_servers_datacenters_workspace_id_datacenter_id",
                table: "servers",
                columns: new[] { "workspace_id", "datacenter_id" },
                principalTable: "datacenters",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_servers_workspaces_workspace_id",
                table: "servers",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_topology_nodes_topology_nodes_workspace_id_parent_node_id",
                table: "topology_nodes",
                columns: new[] { "workspace_id", "parent_node_id" },
                principalTable: "topology_nodes",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_topology_nodes_workspaces_workspace_id",
                table: "topology_nodes",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"app_dependencies\" DROP CONSTRAINT IF EXISTS \"FK_app_dependencies_applications_workspace_id_dest_app_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"app_dependencies\" DROP CONSTRAINT IF EXISTS \"FK_app_dependencies_applications_workspace_id_source_app_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"app_dependencies\" DROP CONSTRAINT IF EXISTS \"FK_app_dependencies_port_mappings_workspace_id_dest_port_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"app_dependencies\" DROP CONSTRAINT IF EXISTS \"FK_app_dependencies_workspaces_workspace_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"applications\" DROP CONSTRAINT IF EXISTS \"FK_applications_workspaces_workspace_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"datacenters\" DROP CONSTRAINT IF EXISTS \"FK_datacenters_workspaces_workspace_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"labels\" DROP CONSTRAINT IF EXISTS \"FK_labels_workspaces_workspace_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"port_mappings\" DROP CONSTRAINT IF EXISTS \"FK_port_mappings_applications_workspace_id_app_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"port_mappings\" DROP CONSTRAINT IF EXISTS \"FK_port_mappings_servers_workspace_id_server_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"port_mappings\" DROP CONSTRAINT IF EXISTS \"FK_port_mappings_workspaces_workspace_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"servers\" DROP CONSTRAINT IF EXISTS \"FK_servers_datacenters_workspace_id_datacenter_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"servers\" DROP CONSTRAINT IF EXISTS \"FK_servers_workspaces_workspace_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"topology_nodes\" DROP CONSTRAINT IF EXISTS \"FK_topology_nodes_topology_nodes_workspace_id_parent_node_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS \"topology_nodes\" DROP CONSTRAINT IF EXISTS \"FK_topology_nodes_workspaces_workspace_id\";");

            migrationBuilder.DropTable(
                name: "workspace_members");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_workspaces_owner_user_id\";");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_topology_nodes_workspace_id_id",
                table: "topology_nodes");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_topology_nodes_workspace_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_topology_nodes_workspace_id_parent_node_id\";");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_servers_workspace_id_id",
                table: "servers");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_servers_workspace_id_datacenter_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_servers_workspace_id_ip_address\";");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_port_mappings_workspace_id_id",
                table: "port_mappings");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_port_mappings_workspace_id_app_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_port_mappings_workspace_id_server_id_port_number\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_labels_workspace_id_key_value\";");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_datacenters_workspace_id_id",
                table: "datacenters");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_datacenters_workspace_id\";");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_applications_workspace_id_id",
                table: "applications");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_applications_workspace_id_app_code\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_app_dependencies_workspace_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_app_dependencies_workspace_id_dest_app_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_app_dependencies_workspace_id_dest_port_id\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_app_dependencies_workspace_id_source_app_id\";");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "is_personal",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "datacenters");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "workspaces",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.CreateIndex(
                name: "IX_topology_nodes_parent_node_id",
                table: "topology_nodes",
                column: "parent_node_id");

            migrationBuilder.CreateIndex(
                name: "IX_servers_datacenter_id",
                table: "servers",
                column: "datacenter_id");

            migrationBuilder.CreateIndex(
                name: "IX_servers_ip_address",
                table: "servers",
                column: "ip_address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_app_id",
                table: "port_mappings",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_server_id",
                table: "port_mappings",
                column: "server_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_app_code",
                table: "applications",
                column: "app_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_dest_app_id",
                table: "app_dependencies",
                column: "dest_app_id");

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_dest_port_id",
                table: "app_dependencies",
                column: "dest_port_id");

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_source_app_id",
                table: "app_dependencies",
                column: "source_app_id");

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_applications_dest_app_id",
                table: "app_dependencies",
                column: "dest_app_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_applications_source_app_id",
                table: "app_dependencies",
                column: "source_app_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_port_mappings_dest_port_id",
                table: "app_dependencies",
                column: "dest_port_id",
                principalTable: "port_mappings",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_port_mappings_applications_app_id",
                table: "port_mappings",
                column: "app_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_port_mappings_servers_server_id",
                table: "port_mappings",
                column: "server_id",
                principalTable: "servers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_servers_datacenters_datacenter_id",
                table: "servers",
                column: "datacenter_id",
                principalTable: "datacenters",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_topology_nodes_topology_nodes_parent_node_id",
                table: "topology_nodes",
                column: "parent_node_id",
                principalTable: "topology_nodes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalCatalogOwnershipFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "topology_nodes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "topology_edges",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "servers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "server_labels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "port_mappings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "labels",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<bool>(
                name: "is_protected",
                table: "labels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "labels",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "business");

            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "labels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "labels",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "datacenters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "applications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "application_labels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_user_id",
                table: "app_dependencies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "label_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grantee_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    permission = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_grants", x => x.id);
                    table.CheckConstraint("ck_label_grants_anonymous_viewer", "token_hash IS NULL OR permission = 'viewer'");
                    table.CheckConstraint("ck_label_grants_permission", "permission IN ('viewer', 'editor')");
                    table.CheckConstraint("ck_label_grants_subject", "(grantee_user_id IS NOT NULL AND token_hash IS NULL) OR (grantee_user_id IS NULL AND token_hash IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_label_grants_labels_label_id",
                        column: x => x.label_id,
                        principalTable: "labels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "owner_catalog_states",
                columns: table => new
                {
                    owner_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    topology_version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_owner_catalog_states", x => x.owner_user_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_topology_nodes_owner_user_id",
                table: "topology_nodes",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_topology_edges_owner_user_id",
                table: "topology_edges",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_servers_owner_user_id",
                table: "servers",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_server_labels_owner_user_id_label_id",
                table: "server_labels",
                columns: new[] { "owner_user_id", "label_id" });

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_owner_user_id",
                table: "port_mappings",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_labels_owner_user_id",
                table: "labels",
                column: "owner_user_id",
                unique: true,
                filter: "kind = 'owner' AND owner_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_labels_owner_user_id_key_value",
                table: "labels",
                columns: new[] { "owner_user_id", "key", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_labels_owner_user_id_kind",
                table: "labels",
                columns: new[] { "owner_user_id", "kind" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_labels_kind",
                table: "labels",
                sql: "kind IN ('owner', 'business')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_labels_owner_protected",
                table: "labels",
                sql: "kind <> 'owner' OR is_protected");

            migrationBuilder.CreateIndex(
                name: "IX_datacenters_owner_user_id",
                table: "datacenters",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_owner_user_id",
                table: "applications",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_labels_owner_user_id_label_id",
                table: "application_labels",
                columns: new[] { "owner_user_id", "label_id" });

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_owner_user_id",
                table: "app_dependencies",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_label_grants_grantee_user_id_revoked_at_expires_at_label_id",
                table: "label_grants",
                columns: new[] { "grantee_user_id", "revoked_at", "expires_at", "label_id" });

            migrationBuilder.CreateIndex(
                name: "IX_label_grants_label_id_grantee_user_id",
                table: "label_grants",
                columns: new[] { "label_id", "grantee_user_id" },
                unique: true,
                filter: "revoked_at IS NULL AND grantee_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_label_grants_owner_user_id_label_id_revoked_at",
                table: "label_grants",
                columns: new[] { "owner_user_id", "label_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_label_grants_token_hash",
                table: "label_grants",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL cannot remove an appended view column with CREATE OR REPLACE VIEW.
            // Recreate both legacy Workspace views before owner_user_id columns are dropped.
            // DROP VIEW uses RESTRICT (the default) so unknown dependents fail the rollback
            // rather than being removed through CASCADE.
            migrationBuilder.Sql(
                """
                DROP VIEW IF EXISTS v_topology_map;
                CREATE OR REPLACE VIEW v_topology_map AS
                SELECT
                    server.workspace_id,
                    server.id AS server_id,
                    server.hostname AS server_hostname,
                    server.ip_address AS server_ip,
                    application.id AS app_id,
                    application.app_name,
                    application.app_code,
                    mapping.port_number,
                    mapping.protocol,
                    server.environment,
                    server.datacenter_id
                FROM servers server
                JOIN port_mappings mapping
                  ON mapping.workspace_id = server.workspace_id
                 AND mapping.server_id = server.id
                JOIN applications application
                  ON application.workspace_id = mapping.workspace_id
                 AND application.id = mapping.app_id;

                DROP VIEW IF EXISTS v_dependency_graph;
                CREATE OR REPLACE VIEW v_dependency_graph AS
                SELECT
                    dependency.workspace_id,
                    source_application.id AS source_app_id,
                    source_application.app_name AS source_app_name,
                    source_application.app_code AS source_app_code,
                    destination_application.id AS dest_app_id,
                    destination_application.app_name AS dest_app_name,
                    destination_application.app_code AS dest_app_code,
                    destination_port.port_number AS dest_port_number,
                    dependency.connection_type,
                    destination_server.hostname AS dest_server_hostname,
                    destination_server.environment,
                    destination_server.datacenter_id
                FROM app_dependencies dependency
                JOIN applications source_application
                  ON source_application.workspace_id = dependency.workspace_id
                 AND source_application.id = dependency.source_app_id
                JOIN applications destination_application
                  ON destination_application.workspace_id = dependency.workspace_id
                 AND destination_application.id = dependency.dest_app_id
                JOIN port_mappings destination_port
                  ON destination_port.workspace_id = dependency.workspace_id
                 AND destination_port.id = dependency.dest_port_id
                JOIN servers destination_server
                  ON destination_server.workspace_id = destination_port.workspace_id
                 AND destination_server.id = destination_port.server_id;
                """);

            migrationBuilder.DropTable(
                name: "label_grants");

            migrationBuilder.DropTable(
                name: "owner_catalog_states");

            migrationBuilder.DropIndex(
                name: "IX_topology_nodes_owner_user_id",
                table: "topology_nodes");

            migrationBuilder.DropIndex(
                name: "IX_topology_edges_owner_user_id",
                table: "topology_edges");

            migrationBuilder.DropIndex(
                name: "IX_servers_owner_user_id",
                table: "servers");

            migrationBuilder.DropIndex(
                name: "IX_server_labels_owner_user_id_label_id",
                table: "server_labels");

            migrationBuilder.DropIndex(
                name: "IX_port_mappings_owner_user_id",
                table: "port_mappings");

            migrationBuilder.DropIndex(
                name: "IX_labels_owner_user_id",
                table: "labels");

            migrationBuilder.DropIndex(
                name: "IX_labels_owner_user_id_key_value",
                table: "labels");

            migrationBuilder.DropIndex(
                name: "IX_labels_owner_user_id_kind",
                table: "labels");

            migrationBuilder.DropCheckConstraint(
                name: "ck_labels_kind",
                table: "labels");

            migrationBuilder.DropCheckConstraint(
                name: "ck_labels_owner_protected",
                table: "labels");

            migrationBuilder.DropIndex(
                name: "IX_datacenters_owner_user_id",
                table: "datacenters");

            migrationBuilder.DropIndex(
                name: "IX_applications_owner_user_id",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "IX_application_labels_owner_user_id_label_id",
                table: "application_labels");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_owner_user_id",
                table: "app_dependencies");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "topology_nodes");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "topology_edges");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "servers");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "server_labels");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "port_mappings");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "labels");

            migrationBuilder.DropColumn(
                name: "is_protected",
                table: "labels");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "labels");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "labels");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "labels");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "datacenters");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "application_labels");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "app_dependencies");
        }
    }
}

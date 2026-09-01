using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWorkspaceRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 8 must reset the local catalog before this destructive cutover. Refuse to
            // drop Workspace metadata while any legacy or owner-catalog data is still present.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM workspaces
                        UNION ALL SELECT 1 FROM workspace_members
                        UNION ALL SELECT 1 FROM workspace_member_scopes
                        UNION ALL SELECT 1 FROM datacenters
                        UNION ALL SELECT 1 FROM servers
                        UNION ALL SELECT 1 FROM applications
                        UNION ALL SELECT 1 FROM port_mappings
                        UNION ALL SELECT 1 FROM app_dependencies
                        UNION ALL SELECT 1 FROM labels
                        UNION ALL SELECT 1 FROM server_labels
                        UNION ALL SELECT 1 FROM application_labels
                        UNION ALL SELECT 1 FROM topology_nodes
                        UNION ALL SELECT 1 FROM topology_edges
                        UNION ALL SELECT 1 FROM label_grants
                        UNION ALL SELECT 1 FROM owner_catalog_states
                    ) THEN
                        RAISE EXCEPTION 'RemoveWorkspaceRuntime requires the approved Phase 8 reset; catalog and Workspace tables must be empty.';
                    END IF;
                END $$;
                """);

            // Views reference workspace_id and must be removed before the destructive cutover.
            // RESTRICT is intentional: unknown dependants make the migration fail closed.
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_dependency_graph; DROP VIEW IF EXISTS v_topology_map;");

            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_applications_workspace_id_dest_app_id",
                table: "app_dependencies");

            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_applications_workspace_id_source_app_id",
                table: "app_dependencies");

            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_port_mappings_workspace_id_dest_port_id",
                table: "app_dependencies");

            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_workspaces_workspace_id",
                table: "app_dependencies");

            migrationBuilder.DropForeignKey(
                name: "FK_application_labels_applications_workspace_id_application_id",
                table: "application_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_application_labels_labels_workspace_id_label_id",
                table: "application_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_application_labels_workspaces_workspace_id",
                table: "application_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_applications_workspaces_workspace_id",
                table: "applications");

            migrationBuilder.DropForeignKey(
                name: "FK_datacenters_workspaces_workspace_id",
                table: "datacenters");

            migrationBuilder.DropForeignKey(
                name: "FK_labels_workspaces_workspace_id",
                table: "labels");

            migrationBuilder.DropForeignKey(
                name: "FK_port_mappings_applications_workspace_id_app_id",
                table: "port_mappings");

            migrationBuilder.DropForeignKey(
                name: "FK_port_mappings_servers_workspace_id_server_id",
                table: "port_mappings");

            migrationBuilder.DropForeignKey(
                name: "FK_port_mappings_workspaces_workspace_id",
                table: "port_mappings");

            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_labels_workspace_id_label_id",
                table: "server_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_servers_workspace_id_server_id",
                table: "server_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_workspaces_workspace_id",
                table: "server_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_servers_datacenters_workspace_id_datacenter_id",
                table: "servers");

            migrationBuilder.DropForeignKey(
                name: "FK_servers_workspaces_workspace_id",
                table: "servers");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_edges_topology_nodes_workspace_id_source_node_id",
                table: "topology_edges");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_edges_topology_nodes_workspace_id_target_node_id",
                table: "topology_edges");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_edges_workspaces_workspace_id",
                table: "topology_edges");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_nodes_topology_nodes_workspace_id_parent_node_id",
                table: "topology_nodes");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_nodes_workspaces_workspace_id",
                table: "topology_nodes");

            migrationBuilder.DropTable(
                name: "workspace_member_scopes");

            migrationBuilder.DropTable(
                name: "workspace_members");

            migrationBuilder.DropTable(
                name: "workspaces");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_topology_nodes_workspace_id_id",
                table: "topology_nodes");

            migrationBuilder.DropIndex(
                name: "IX_topology_nodes_workspace_id",
                table: "topology_nodes");

            migrationBuilder.DropIndex(
                name: "IX_topology_nodes_workspace_id_parent_node_id",
                table: "topology_nodes");

            migrationBuilder.DropIndex(
                name: "IX_topology_edges_owner_user_id",
                table: "topology_edges");

            migrationBuilder.DropIndex(
                name: "IX_topology_edges_workspace_id_source_node_id_target_node_id_s~",
                table: "topology_edges");

            migrationBuilder.DropIndex(
                name: "IX_topology_edges_workspace_id_target_node_id",
                table: "topology_edges");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_servers_workspace_id_id",
                table: "servers");

            migrationBuilder.DropIndex(
                name: "IX_servers_owner_user_id",
                table: "servers");

            migrationBuilder.DropIndex(
                name: "IX_servers_workspace_id_datacenter_id",
                table: "servers");

            migrationBuilder.DropIndex(
                name: "IX_servers_workspace_id_ip_address",
                table: "servers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_server_labels",
                table: "server_labels");

            migrationBuilder.DropIndex(
                name: "IX_server_labels_workspace_id_label_id",
                table: "server_labels");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_port_mappings_workspace_id_id",
                table: "port_mappings");

            migrationBuilder.DropIndex(
                name: "IX_port_mappings_owner_user_id",
                table: "port_mappings");

            migrationBuilder.DropIndex(
                name: "IX_port_mappings_workspace_id_app_id",
                table: "port_mappings");

            migrationBuilder.DropIndex(
                name: "IX_port_mappings_workspace_id_server_id_port_number",
                table: "port_mappings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_labels_workspace_id_id",
                table: "labels");

            migrationBuilder.DropIndex(
                name: "IX_labels_workspace_id_key_value",
                table: "labels");

            migrationBuilder.DropIndex(
                name: "IX_labels_workspace_id_owner_user_id_key_value",
                table: "labels");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_datacenters_workspace_id_id",
                table: "datacenters");

            migrationBuilder.DropIndex(
                name: "IX_datacenters_workspace_id",
                table: "datacenters");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_applications_workspace_id_id",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "IX_applications_owner_user_id",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "IX_applications_workspace_id_app_code",
                table: "applications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_application_labels",
                table: "application_labels");

            migrationBuilder.DropIndex(
                name: "IX_application_labels_workspace_id_label_id",
                table: "application_labels");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_owner_user_id",
                table: "app_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_workspace_id",
                table: "app_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_workspace_id_dest_app_id",
                table: "app_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_workspace_id_dest_port_id",
                table: "app_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_workspace_id_source_app_id_dest_app_id_des~",
                table: "app_dependencies");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "topology_nodes");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "topology_edges");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "servers");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "server_labels");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "port_mappings");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "labels");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "datacenters");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "application_labels");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "app_dependencies");

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "topology_nodes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "topology_edges",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "servers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "server_labels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "port_mappings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "labels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "datacenters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "applications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "application_labels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "app_dependencies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_server_labels",
                table: "server_labels",
                columns: new[] { "server_id", "label_id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_application_labels",
                table: "application_labels",
                columns: new[] { "application_id", "label_id" });

            migrationBuilder.CreateIndex(
                name: "IX_topology_nodes_parent_node_id",
                table: "topology_nodes",
                column: "parent_node_id");

            migrationBuilder.CreateIndex(
                name: "IX_topology_edges_owner_user_id_source_node_id_target_node_id_~",
                table: "topology_edges",
                columns: new[] { "owner_user_id", "source_node_id", "target_node_id", "source_handle", "target_handle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topology_edges_source_node_id",
                table: "topology_edges",
                column: "source_node_id");

            migrationBuilder.CreateIndex(
                name: "IX_topology_edges_target_node_id",
                table: "topology_edges",
                column: "target_node_id");

            migrationBuilder.CreateIndex(
                name: "IX_servers_datacenter_id",
                table: "servers",
                column: "datacenter_id");

            migrationBuilder.CreateIndex(
                name: "IX_servers_owner_user_id_ip_address",
                table: "servers",
                columns: new[] { "owner_user_id", "ip_address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_server_labels_label_id",
                table: "server_labels",
                column: "label_id");

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_app_id",
                table: "port_mappings",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_owner_user_id_server_id_port_number",
                table: "port_mappings",
                columns: new[] { "owner_user_id", "server_id", "port_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_server_id",
                table: "port_mappings",
                column: "server_id");

            migrationBuilder.CreateIndex(
                name: "IX_labels_owner_user_id_key_value",
                table: "labels",
                columns: new[] { "owner_user_id", "key", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_applications_owner_user_id_app_code",
                table: "applications",
                columns: new[] { "owner_user_id", "app_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_labels_label_id",
                table: "application_labels",
                column: "label_id");

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_dest_app_id",
                table: "app_dependencies",
                column: "dest_app_id");

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_dest_port_id",
                table: "app_dependencies",
                column: "dest_port_id");

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_owner_user_id_source_app_id_dest_app_id_de~",
                table: "app_dependencies",
                columns: new[] { "owner_user_id", "source_app_id", "dest_app_id", "dest_port_id" },
                unique: true);

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
                name: "FK_application_labels_applications_application_id",
                table: "application_labels",
                column: "application_id",
                principalTable: "applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_application_labels_labels_label_id",
                table: "application_labels",
                column: "label_id",
                principalTable: "labels",
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
                name: "FK_server_labels_labels_label_id",
                table: "server_labels",
                column: "label_id",
                principalTable: "labels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_server_labels_servers_server_id",
                table: "server_labels",
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
                name: "FK_topology_edges_topology_nodes_source_node_id",
                table: "topology_edges",
                column: "source_node_id",
                principalTable: "topology_nodes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_topology_edges_topology_nodes_target_node_id",
                table: "topology_edges",
                column: "target_node_id",
                principalTable: "topology_nodes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_topology_nodes_topology_nodes_parent_node_id",
                table: "topology_nodes",
                column: "parent_node_id",
                principalTable: "topology_nodes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql(
                """
                CREATE VIEW v_topology_map AS
                SELECT
                    server.id AS server_id,
                    server.hostname AS server_hostname,
                    server.ip_address AS server_ip,
                    application.id AS app_id,
                    application.app_name,
                    application.app_code,
                    mapping.port_number,
                    mapping.protocol,
                    server.environment,
                    server.datacenter_id,
                    server.owner_user_id
                FROM servers server
                JOIN port_mappings mapping ON mapping.server_id = server.id
                    AND mapping.owner_user_id = server.owner_user_id
                JOIN applications application ON application.id = mapping.app_id
                    AND application.owner_user_id = mapping.owner_user_id;

                CREATE VIEW v_dependency_graph AS
                SELECT
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
                    destination_server.datacenter_id,
                    dependency.owner_user_id
                FROM app_dependencies dependency
                JOIN applications source_application ON source_application.id = dependency.source_app_id
                    AND source_application.owner_user_id = dependency.owner_user_id
                JOIN applications destination_application ON destination_application.id = dependency.dest_app_id
                    AND destination_application.owner_user_id = dependency.owner_user_id
                JOIN port_mappings destination_port ON destination_port.id = dependency.dest_port_id
                    AND destination_port.owner_user_id = dependency.owner_user_id
                JOIN servers destination_server ON destination_server.id = destination_port.server_id
                    AND destination_server.owner_user_id = dependency.owner_user_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_dependency_graph; DROP VIEW IF EXISTS v_topology_map;");

            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_applications_dest_app_id",
                table: "app_dependencies");

            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_applications_source_app_id",
                table: "app_dependencies");

            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_port_mappings_dest_port_id",
                table: "app_dependencies");

            migrationBuilder.DropForeignKey(
                name: "FK_application_labels_applications_application_id",
                table: "application_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_application_labels_labels_label_id",
                table: "application_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_port_mappings_applications_app_id",
                table: "port_mappings");

            migrationBuilder.DropForeignKey(
                name: "FK_port_mappings_servers_server_id",
                table: "port_mappings");

            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_labels_label_id",
                table: "server_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_servers_server_id",
                table: "server_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_servers_datacenters_datacenter_id",
                table: "servers");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_edges_topology_nodes_source_node_id",
                table: "topology_edges");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_edges_topology_nodes_target_node_id",
                table: "topology_edges");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_nodes_topology_nodes_parent_node_id",
                table: "topology_nodes");

            migrationBuilder.DropIndex(
                name: "IX_topology_nodes_parent_node_id",
                table: "topology_nodes");

            migrationBuilder.DropIndex(
                name: "IX_topology_edges_owner_user_id_source_node_id_target_node_id_~",
                table: "topology_edges");

            migrationBuilder.DropIndex(
                name: "IX_topology_edges_source_node_id",
                table: "topology_edges");

            migrationBuilder.DropIndex(
                name: "IX_topology_edges_target_node_id",
                table: "topology_edges");

            migrationBuilder.DropIndex(
                name: "IX_servers_datacenter_id",
                table: "servers");

            migrationBuilder.DropIndex(
                name: "IX_servers_owner_user_id_ip_address",
                table: "servers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_server_labels",
                table: "server_labels");

            migrationBuilder.DropIndex(
                name: "IX_server_labels_label_id",
                table: "server_labels");

            migrationBuilder.DropIndex(
                name: "IX_port_mappings_app_id",
                table: "port_mappings");

            migrationBuilder.DropIndex(
                name: "IX_port_mappings_owner_user_id_server_id_port_number",
                table: "port_mappings");

            migrationBuilder.DropIndex(
                name: "IX_port_mappings_server_id",
                table: "port_mappings");

            migrationBuilder.DropIndex(
                name: "IX_labels_owner_user_id_key_value",
                table: "labels");

            migrationBuilder.DropIndex(
                name: "IX_applications_owner_user_id_app_code",
                table: "applications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_application_labels",
                table: "application_labels");

            migrationBuilder.DropIndex(
                name: "IX_application_labels_label_id",
                table: "application_labels");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_dest_app_id",
                table: "app_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_dest_port_id",
                table: "app_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_owner_user_id_source_app_id_dest_app_id_de~",
                table: "app_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_source_app_id",
                table: "app_dependencies");

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "topology_nodes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "topology_nodes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "topology_edges",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "topology_edges",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "servers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "servers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "server_labels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "server_labels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "port_mappings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "port_mappings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "labels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "labels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "datacenters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "datacenters",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "applications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "applications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "application_labels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "application_labels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "owner_user_id",
                table: "app_dependencies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "app_dependencies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_topology_nodes_workspace_id_id",
                table: "topology_nodes",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_servers_workspace_id_id",
                table: "servers",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_server_labels",
                table: "server_labels",
                columns: new[] { "workspace_id", "server_id", "label_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_port_mappings_workspace_id_id",
                table: "port_mappings",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_labels_workspace_id_id",
                table: "labels",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_datacenters_workspace_id_id",
                table: "datacenters",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_applications_workspace_id_id",
                table: "applications",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_application_labels",
                table: "application_labels",
                columns: new[] { "workspace_id", "application_id", "label_id" });

            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_personal = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    owner_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    topology_version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_members",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    invited_by_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    scope_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_members", x => new { x.workspace_id, x.user_id });
                    table.CheckConstraint("ck_workspace_members_admin_all", "role <> 'workspace_admin' OR scope_mode = 'all'");
                    table.CheckConstraint("ck_workspace_members_role", "role IN ('workspace_admin', 'auditor', 'viewer')");
                    table.CheckConstraint("ck_workspace_members_scope_mode", "scope_mode IN ('all', 'labels', 'frames')");
                    table.ForeignKey(
                        name: "FK_workspace_members_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_member_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_member_scopes", x => x.id);
                    table.CheckConstraint("ck_workspace_member_scopes_target", "target_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_workspace_member_scopes_type", "scope_type IN ('label', 'frame')");
                    table.ForeignKey(
                        name: "FK_workspace_member_scopes_workspace_members_workspace_id_user~",
                        columns: x => new { x.workspace_id, x.user_id },
                        principalTable: "workspace_members",
                        principalColumns: new[] { "workspace_id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_topology_nodes_workspace_id",
                table: "topology_nodes",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_topology_nodes_workspace_id_parent_node_id",
                table: "topology_nodes",
                columns: new[] { "workspace_id", "parent_node_id" });

            migrationBuilder.CreateIndex(
                name: "IX_topology_edges_owner_user_id",
                table: "topology_edges",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_topology_edges_workspace_id_source_node_id_target_node_id_s~",
                table: "topology_edges",
                columns: new[] { "workspace_id", "source_node_id", "target_node_id", "source_handle", "target_handle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topology_edges_workspace_id_target_node_id",
                table: "topology_edges",
                columns: new[] { "workspace_id", "target_node_id" });

            migrationBuilder.CreateIndex(
                name: "IX_servers_owner_user_id",
                table: "servers",
                column: "owner_user_id");

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
                name: "IX_server_labels_workspace_id_label_id",
                table: "server_labels",
                columns: new[] { "workspace_id", "label_id" });

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_owner_user_id",
                table: "port_mappings",
                column: "owner_user_id");

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
                name: "IX_labels_workspace_id_owner_user_id_key_value",
                table: "labels",
                columns: new[] { "workspace_id", "owner_user_id", "key", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_datacenters_workspace_id",
                table: "datacenters",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_owner_user_id",
                table: "applications",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_workspace_id_app_code",
                table: "applications",
                columns: new[] { "workspace_id", "app_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_labels_workspace_id_label_id",
                table: "application_labels",
                columns: new[] { "workspace_id", "label_id" });

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_owner_user_id",
                table: "app_dependencies",
                column: "owner_user_id");

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
                name: "IX_app_dependencies_workspace_id_source_app_id_dest_app_id_des~",
                table: "app_dependencies",
                columns: new[] { "workspace_id", "source_app_id", "dest_app_id", "dest_port_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_member_scopes_workspace_id_user_id",
                table: "workspace_member_scopes",
                columns: new[] { "workspace_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_member_scopes_workspace_id_user_id_scope_type_tar~",
                table: "workspace_member_scopes",
                columns: new[] { "workspace_id", "user_id", "scope_type", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_members_user_id",
                table: "workspace_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_owner_user_id",
                table: "workspaces",
                column: "owner_user_id",
                unique: true,
                filter: "is_personal = true");

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
                name: "FK_application_labels_applications_workspace_id_application_id",
                table: "application_labels",
                columns: new[] { "workspace_id", "application_id" },
                principalTable: "applications",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_application_labels_labels_workspace_id_label_id",
                table: "application_labels",
                columns: new[] { "workspace_id", "label_id" },
                principalTable: "labels",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_application_labels_workspaces_workspace_id",
                table: "application_labels",
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
                name: "FK_server_labels_labels_workspace_id_label_id",
                table: "server_labels",
                columns: new[] { "workspace_id", "label_id" },
                principalTable: "labels",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_server_labels_servers_workspace_id_server_id",
                table: "server_labels",
                columns: new[] { "workspace_id", "server_id" },
                principalTable: "servers",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_server_labels_workspaces_workspace_id",
                table: "server_labels",
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
                name: "FK_topology_edges_topology_nodes_workspace_id_source_node_id",
                table: "topology_edges",
                columns: new[] { "workspace_id", "source_node_id" },
                principalTable: "topology_nodes",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_topology_edges_topology_nodes_workspace_id_target_node_id",
                table: "topology_edges",
                columns: new[] { "workspace_id", "target_node_id" },
                principalTable: "topology_nodes",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_topology_edges_workspaces_workspace_id",
                table: "topology_edges",
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

            migrationBuilder.Sql(
                """
                CREATE VIEW v_topology_map AS
                SELECT server.workspace_id, server.id AS server_id, server.hostname AS server_hostname,
                       server.ip_address AS server_ip, application.id AS app_id, application.app_name,
                       application.app_code, mapping.port_number, mapping.protocol, server.environment,
                       server.datacenter_id, server.owner_user_id
                FROM servers server
                JOIN port_mappings mapping ON mapping.workspace_id = server.workspace_id AND mapping.server_id = server.id
                JOIN applications application ON application.workspace_id = mapping.workspace_id AND application.id = mapping.app_id;

                CREATE VIEW v_dependency_graph AS
                SELECT dependency.workspace_id, source_application.id AS source_app_id,
                       source_application.app_name AS source_app_name, source_application.app_code AS source_app_code,
                       destination_application.id AS dest_app_id, destination_application.app_name AS dest_app_name,
                       destination_application.app_code AS dest_app_code, destination_port.port_number AS dest_port_number,
                       dependency.connection_type, destination_server.hostname AS dest_server_hostname,
                       destination_server.environment, destination_server.datacenter_id, dependency.owner_user_id
                FROM app_dependencies dependency
                JOIN applications source_application ON source_application.workspace_id = dependency.workspace_id AND source_application.id = dependency.source_app_id
                JOIN applications destination_application ON destination_application.workspace_id = dependency.workspace_id AND destination_application.id = dependency.dest_app_id
                JOIN port_mappings destination_port ON destination_port.workspace_id = dependency.workspace_id AND destination_port.id = dependency.dest_port_id
                JOIN servers destination_server ON destination_server.workspace_id = destination_port.workspace_id AND destination_server.id = destination_port.server_id;
                """);
        }
    }
}

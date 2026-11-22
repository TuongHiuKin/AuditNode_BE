using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOwnerScopedRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "FK_label_grants_labels_label_id",
                table: "label_grants");

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
                name: "IX_topology_edges_source_node_id",
                table: "topology_edges");

            migrationBuilder.DropIndex(
                name: "IX_topology_edges_target_node_id",
                table: "topology_edges");

            migrationBuilder.DropIndex(
                name: "IX_servers_datacenter_id",
                table: "servers");

            migrationBuilder.DropIndex(
                name: "IX_server_labels_label_id",
                table: "server_labels");

            migrationBuilder.DropIndex(
                name: "IX_port_mappings_app_id",
                table: "port_mappings");

            migrationBuilder.DropIndex(
                name: "IX_port_mappings_server_id",
                table: "port_mappings");

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
                name: "IX_app_dependencies_source_app_id",
                table: "app_dependencies");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_topology_nodes_owner_user_id_id",
                table: "topology_nodes",
                columns: new[] { "owner_user_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_topology_edges_owner_user_id_id",
                table: "topology_edges",
                columns: new[] { "owner_user_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_servers_owner_user_id_id",
                table: "servers",
                columns: new[] { "owner_user_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_port_mappings_owner_user_id_id",
                table: "port_mappings",
                columns: new[] { "owner_user_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_labels_owner_user_id_id",
                table: "labels",
                columns: new[] { "owner_user_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_datacenters_owner_user_id_id",
                table: "datacenters",
                columns: new[] { "owner_user_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_applications_owner_user_id_id",
                table: "applications",
                columns: new[] { "owner_user_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_app_dependencies_owner_user_id_id",
                table: "app_dependencies",
                columns: new[] { "owner_user_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_topology_nodes_owner_user_id_parent_node_id",
                table: "topology_nodes",
                columns: new[] { "owner_user_id", "parent_node_id" });

            migrationBuilder.CreateIndex(
                name: "IX_topology_edges_owner_user_id_target_node_id",
                table: "topology_edges",
                columns: new[] { "owner_user_id", "target_node_id" });

            migrationBuilder.CreateIndex(
                name: "IX_servers_owner_user_id_datacenter_id",
                table: "servers",
                columns: new[] { "owner_user_id", "datacenter_id" });

            migrationBuilder.CreateIndex(
                name: "IX_server_labels_owner_user_id_server_id",
                table: "server_labels",
                columns: new[] { "owner_user_id", "server_id" });

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_owner_user_id_app_id",
                table: "port_mappings",
                columns: new[] { "owner_user_id", "app_id" });

            migrationBuilder.CreateIndex(
                name: "IX_application_labels_owner_user_id_application_id",
                table: "application_labels",
                columns: new[] { "owner_user_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_owner_user_id_dest_app_id",
                table: "app_dependencies",
                columns: new[] { "owner_user_id", "dest_app_id" });

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_owner_user_id_dest_port_id",
                table: "app_dependencies",
                columns: new[] { "owner_user_id", "dest_port_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_applications_owner_user_id_dest_app_id",
                table: "app_dependencies",
                columns: new[] { "owner_user_id", "dest_app_id" },
                principalTable: "applications",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_applications_owner_user_id_source_app_id",
                table: "app_dependencies",
                columns: new[] { "owner_user_id", "source_app_id" },
                principalTable: "applications",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_port_mappings_owner_user_id_dest_port_id",
                table: "app_dependencies",
                columns: new[] { "owner_user_id", "dest_port_id" },
                principalTable: "port_mappings",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_application_labels_applications_owner_user_id_application_id",
                table: "application_labels",
                columns: new[] { "owner_user_id", "application_id" },
                principalTable: "applications",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_application_labels_labels_owner_user_id_label_id",
                table: "application_labels",
                columns: new[] { "owner_user_id", "label_id" },
                principalTable: "labels",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_label_grants_labels_owner_user_id_label_id",
                table: "label_grants",
                columns: new[] { "owner_user_id", "label_id" },
                principalTable: "labels",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_port_mappings_applications_owner_user_id_app_id",
                table: "port_mappings",
                columns: new[] { "owner_user_id", "app_id" },
                principalTable: "applications",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_port_mappings_servers_owner_user_id_server_id",
                table: "port_mappings",
                columns: new[] { "owner_user_id", "server_id" },
                principalTable: "servers",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_server_labels_labels_owner_user_id_label_id",
                table: "server_labels",
                columns: new[] { "owner_user_id", "label_id" },
                principalTable: "labels",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_server_labels_servers_owner_user_id_server_id",
                table: "server_labels",
                columns: new[] { "owner_user_id", "server_id" },
                principalTable: "servers",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_servers_datacenters_owner_user_id_datacenter_id",
                table: "servers",
                columns: new[] { "owner_user_id", "datacenter_id" },
                principalTable: "datacenters",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_topology_edges_topology_nodes_owner_user_id_source_node_id",
                table: "topology_edges",
                columns: new[] { "owner_user_id", "source_node_id" },
                principalTable: "topology_nodes",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_topology_edges_topology_nodes_owner_user_id_target_node_id",
                table: "topology_edges",
                columns: new[] { "owner_user_id", "target_node_id" },
                principalTable: "topology_nodes",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_topology_nodes_topology_nodes_owner_user_id_parent_node_id",
                table: "topology_nodes",
                columns: new[] { "owner_user_id", "parent_node_id" },
                principalTable: "topology_nodes",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_applications_owner_user_id_dest_app_id",
                table: "app_dependencies");

            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_applications_owner_user_id_source_app_id",
                table: "app_dependencies");

            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_port_mappings_owner_user_id_dest_port_id",
                table: "app_dependencies");

            migrationBuilder.DropForeignKey(
                name: "FK_application_labels_applications_owner_user_id_application_id",
                table: "application_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_application_labels_labels_owner_user_id_label_id",
                table: "application_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_label_grants_labels_owner_user_id_label_id",
                table: "label_grants");

            migrationBuilder.DropForeignKey(
                name: "FK_port_mappings_applications_owner_user_id_app_id",
                table: "port_mappings");

            migrationBuilder.DropForeignKey(
                name: "FK_port_mappings_servers_owner_user_id_server_id",
                table: "port_mappings");

            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_labels_owner_user_id_label_id",
                table: "server_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_servers_owner_user_id_server_id",
                table: "server_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_servers_datacenters_owner_user_id_datacenter_id",
                table: "servers");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_edges_topology_nodes_owner_user_id_source_node_id",
                table: "topology_edges");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_edges_topology_nodes_owner_user_id_target_node_id",
                table: "topology_edges");

            migrationBuilder.DropForeignKey(
                name: "FK_topology_nodes_topology_nodes_owner_user_id_parent_node_id",
                table: "topology_nodes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_topology_nodes_owner_user_id_id",
                table: "topology_nodes");

            migrationBuilder.DropIndex(
                name: "IX_topology_nodes_owner_user_id_parent_node_id",
                table: "topology_nodes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_topology_edges_owner_user_id_id",
                table: "topology_edges");

            migrationBuilder.DropIndex(
                name: "IX_topology_edges_owner_user_id_target_node_id",
                table: "topology_edges");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_servers_owner_user_id_id",
                table: "servers");

            migrationBuilder.DropIndex(
                name: "IX_servers_owner_user_id_datacenter_id",
                table: "servers");

            migrationBuilder.DropIndex(
                name: "IX_server_labels_owner_user_id_server_id",
                table: "server_labels");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_port_mappings_owner_user_id_id",
                table: "port_mappings");

            migrationBuilder.DropIndex(
                name: "IX_port_mappings_owner_user_id_app_id",
                table: "port_mappings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_labels_owner_user_id_id",
                table: "labels");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_datacenters_owner_user_id_id",
                table: "datacenters");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_applications_owner_user_id_id",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "IX_application_labels_owner_user_id_application_id",
                table: "application_labels");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_app_dependencies_owner_user_id_id",
                table: "app_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_owner_user_id_dest_app_id",
                table: "app_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_owner_user_id_dest_port_id",
                table: "app_dependencies");

            migrationBuilder.CreateIndex(
                name: "IX_topology_nodes_parent_node_id",
                table: "topology_nodes",
                column: "parent_node_id");

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
                name: "IX_server_labels_label_id",
                table: "server_labels",
                column: "label_id");

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_app_id",
                table: "port_mappings",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "IX_port_mappings_server_id",
                table: "port_mappings",
                column: "server_id");

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
                name: "FK_label_grants_labels_label_id",
                table: "label_grants",
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
        }
    }
}

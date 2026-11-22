using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalCatalogOwnershipHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_label_grants_token_expiry",
                table: "label_grants",
                sql: "token_hash IS NULL OR expires_at IS NOT NULL");

            // EF Core alternate keys make their properties required, which would incorrectly
            // force nullable legacy owner_user_id columns to NOT NULL in this transitional phase.
            // PostgreSQL MATCH SIMPLE composite foreign keys enforce every owner-aware row while
            // allowing untouched legacy rows whose newly added owner_user_id remains NULL.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "UX_labels_owner_user_id_id"
                    ON "labels" ("owner_user_id", "id");
                CREATE UNIQUE INDEX "UX_servers_owner_user_id_id"
                    ON "servers" ("owner_user_id", "id");
                CREATE UNIQUE INDEX "UX_applications_owner_user_id_id"
                    ON "applications" ("owner_user_id", "id");
                CREATE UNIQUE INDEX "UX_datacenters_owner_user_id_id"
                    ON "datacenters" ("owner_user_id", "id");
                CREATE UNIQUE INDEX "UX_port_mappings_owner_user_id_id"
                    ON "port_mappings" ("owner_user_id", "id");
                CREATE UNIQUE INDEX "UX_topology_nodes_owner_user_id_id"
                    ON "topology_nodes" ("owner_user_id", "id");

                ALTER TABLE "label_grants"
                    ADD CONSTRAINT "FK_label_grants_labels_owner_label"
                    FOREIGN KEY ("owner_user_id", "label_id")
                    REFERENCES "labels" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;

                ALTER TABLE "server_labels"
                    ADD CONSTRAINT "FK_server_labels_servers_owner_server"
                    FOREIGN KEY ("owner_user_id", "server_id")
                    REFERENCES "servers" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;
                ALTER TABLE "server_labels"
                    ADD CONSTRAINT "FK_server_labels_labels_owner_label"
                    FOREIGN KEY ("owner_user_id", "label_id")
                    REFERENCES "labels" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;

                ALTER TABLE "application_labels"
                    ADD CONSTRAINT "FK_application_labels_applications_owner_app"
                    FOREIGN KEY ("owner_user_id", "application_id")
                    REFERENCES "applications" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;
                ALTER TABLE "application_labels"
                    ADD CONSTRAINT "FK_application_labels_labels_owner_label"
                    FOREIGN KEY ("owner_user_id", "label_id")
                    REFERENCES "labels" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;

                ALTER TABLE "servers"
                    ADD CONSTRAINT "FK_servers_datacenters_owner_datacenter"
                    FOREIGN KEY ("owner_user_id", "datacenter_id")
                    REFERENCES "datacenters" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;

                ALTER TABLE "port_mappings"
                    ADD CONSTRAINT "FK_port_mappings_servers_owner_server"
                    FOREIGN KEY ("owner_user_id", "server_id")
                    REFERENCES "servers" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;
                ALTER TABLE "port_mappings"
                    ADD CONSTRAINT "FK_port_mappings_applications_owner_app"
                    FOREIGN KEY ("owner_user_id", "app_id")
                    REFERENCES "applications" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;

                ALTER TABLE "app_dependencies"
                    ADD CONSTRAINT "FK_app_dependencies_applications_owner_source"
                    FOREIGN KEY ("owner_user_id", "source_app_id")
                    REFERENCES "applications" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;
                ALTER TABLE "app_dependencies"
                    ADD CONSTRAINT "FK_app_dependencies_applications_owner_dest"
                    FOREIGN KEY ("owner_user_id", "dest_app_id")
                    REFERENCES "applications" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;
                ALTER TABLE "app_dependencies"
                    ADD CONSTRAINT "FK_app_dependencies_port_mappings_owner_dest_port"
                    FOREIGN KEY ("owner_user_id", "dest_port_id")
                    REFERENCES "port_mappings" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;

                ALTER TABLE "topology_nodes"
                    ADD CONSTRAINT "FK_topology_nodes_topology_nodes_owner_parent"
                    FOREIGN KEY ("owner_user_id", "parent_node_id")
                    REFERENCES "topology_nodes" ("owner_user_id", "id")
                    ON DELETE CASCADE NOT VALID;

                ALTER TABLE "topology_edges"
                    ADD CONSTRAINT "FK_topology_edges_topology_nodes_owner_source"
                    FOREIGN KEY ("owner_user_id", "source_node_id")
                    REFERENCES "topology_nodes" ("owner_user_id", "id")
                    ON DELETE RESTRICT NOT VALID;
                ALTER TABLE "topology_edges"
                    ADD CONSTRAINT "FK_topology_edges_topology_nodes_owner_target"
                    FOREIGN KEY ("owner_user_id", "target_node_id")
                    REFERENCES "topology_nodes" ("owner_user_id", "id")
                    ON DELETE RESTRICT NOT VALID;

                ALTER TABLE "label_grants"
                    VALIDATE CONSTRAINT "FK_label_grants_labels_owner_label";
                ALTER TABLE "server_labels"
                    VALIDATE CONSTRAINT "FK_server_labels_servers_owner_server";
                ALTER TABLE "server_labels"
                    VALIDATE CONSTRAINT "FK_server_labels_labels_owner_label";
                ALTER TABLE "application_labels"
                    VALIDATE CONSTRAINT "FK_application_labels_applications_owner_app";
                ALTER TABLE "application_labels"
                    VALIDATE CONSTRAINT "FK_application_labels_labels_owner_label";
                ALTER TABLE "servers"
                    VALIDATE CONSTRAINT "FK_servers_datacenters_owner_datacenter";
                ALTER TABLE "port_mappings"
                    VALIDATE CONSTRAINT "FK_port_mappings_servers_owner_server";
                ALTER TABLE "port_mappings"
                    VALIDATE CONSTRAINT "FK_port_mappings_applications_owner_app";
                ALTER TABLE "app_dependencies"
                    VALIDATE CONSTRAINT "FK_app_dependencies_applications_owner_source";
                ALTER TABLE "app_dependencies"
                    VALIDATE CONSTRAINT "FK_app_dependencies_applications_owner_dest";
                ALTER TABLE "app_dependencies"
                    VALIDATE CONSTRAINT "FK_app_dependencies_port_mappings_owner_dest_port";
                ALTER TABLE "topology_nodes"
                    VALIDATE CONSTRAINT "FK_topology_nodes_topology_nodes_owner_parent";
                ALTER TABLE "topology_edges"
                    VALIDATE CONSTRAINT "FK_topology_edges_topology_nodes_owner_source";
                ALTER TABLE "topology_edges"
                    VALIDATE CONSTRAINT "FK_topology_edges_topology_nodes_owner_target";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "label_grants"
                    DROP CONSTRAINT "FK_label_grants_labels_owner_label";
                ALTER TABLE "server_labels"
                    DROP CONSTRAINT "FK_server_labels_servers_owner_server";
                ALTER TABLE "server_labels"
                    DROP CONSTRAINT "FK_server_labels_labels_owner_label";
                ALTER TABLE "application_labels"
                    DROP CONSTRAINT "FK_application_labels_applications_owner_app";
                ALTER TABLE "application_labels"
                    DROP CONSTRAINT "FK_application_labels_labels_owner_label";
                ALTER TABLE "servers"
                    DROP CONSTRAINT "FK_servers_datacenters_owner_datacenter";
                ALTER TABLE "port_mappings"
                    DROP CONSTRAINT "FK_port_mappings_servers_owner_server";
                ALTER TABLE "port_mappings"
                    DROP CONSTRAINT "FK_port_mappings_applications_owner_app";
                ALTER TABLE "app_dependencies"
                    DROP CONSTRAINT "FK_app_dependencies_applications_owner_source";
                ALTER TABLE "app_dependencies"
                    DROP CONSTRAINT "FK_app_dependencies_applications_owner_dest";
                ALTER TABLE "app_dependencies"
                    DROP CONSTRAINT "FK_app_dependencies_port_mappings_owner_dest_port";
                ALTER TABLE "topology_nodes"
                    DROP CONSTRAINT "FK_topology_nodes_topology_nodes_owner_parent";
                ALTER TABLE "topology_edges"
                    DROP CONSTRAINT "FK_topology_edges_topology_nodes_owner_source";
                ALTER TABLE "topology_edges"
                    DROP CONSTRAINT "FK_topology_edges_topology_nodes_owner_target";

                DROP INDEX "UX_labels_owner_user_id_id";
                DROP INDEX "UX_servers_owner_user_id_id";
                DROP INDEX "UX_applications_owner_user_id_id";
                DROP INDEX "UX_datacenters_owner_user_id_id";
                DROP INDEX "UX_port_mappings_owner_user_id_id";
                DROP INDEX "UX_topology_nodes_owner_user_id_id";
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_label_grants_token_expiry",
                table: "label_grants");
        }
    }
}

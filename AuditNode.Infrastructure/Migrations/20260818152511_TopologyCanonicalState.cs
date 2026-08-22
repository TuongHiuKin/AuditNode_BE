using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TopologyCanonicalState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT node.id
                        FROM topology_nodes AS node
                        JOIN applications AS application
                          ON application.workspace_id = node.workspace_id
                         AND application.id = node.reference_id
                        LEFT JOIN port_mappings AS mapping
                          ON mapping.workspace_id = application.workspace_id
                         AND mapping.app_id = application.id
                        WHERE lower(node.node_type) = 'application'
                          AND NOT EXISTS (
                              SELECT 1 FROM port_mappings AS canonical
                              WHERE canonical.workspace_id = node.workspace_id
                                AND canonical.id = node.reference_id
                          )
                        GROUP BY node.workspace_id, node.id
                        HAVING count(mapping.id) > 1
                    ) THEN
                        RAISE EXCEPTION 'Topology migration found an ambiguous legacy application reference; select the intended PortMappingId before continuing.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM topology_nodes AS node
                        WHERE lower(node.node_type) = 'application'
                          AND NOT EXISTS (
                              SELECT 1 FROM port_mappings AS canonical
                              WHERE canonical.workspace_id = node.workspace_id
                                AND canonical.id = node.reference_id
                          )
                          AND (
                              node.reference_id IS NULL
                              OR NOT EXISTS (
                                  SELECT 1
                                  FROM applications AS application
                                  JOIN port_mappings AS mapping
                                    ON mapping.workspace_id = application.workspace_id
                                   AND mapping.app_id = application.id
                                  WHERE application.workspace_id = node.workspace_id
                                    AND application.id = node.reference_id
                              )
                          )
                    ) THEN
                        RAISE EXCEPTION 'Topology migration found an unresolvable legacy application reference; backfill or explicitly reset the node before continuing.';
                    END IF;

                    UPDATE topology_nodes AS node
                    SET reference_id = candidate.mapping_id
                    FROM (
                        SELECT node.workspace_id,
                               node.id AS node_id,
                               min(mapping.id::text)::uuid AS mapping_id
                        FROM topology_nodes AS node
                        JOIN applications AS application
                          ON application.workspace_id = node.workspace_id
                         AND application.id = node.reference_id
                        JOIN port_mappings AS mapping
                          ON mapping.workspace_id = application.workspace_id
                         AND mapping.app_id = application.id
                        WHERE lower(node.node_type) = 'application'
                          AND NOT EXISTS (
                              SELECT 1 FROM port_mappings AS canonical
                              WHERE canonical.workspace_id = node.workspace_id
                                AND canonical.id = node.reference_id
                          )
                        GROUP BY node.workspace_id, node.id
                        HAVING count(mapping.id) = 1
                    ) AS candidate
                    WHERE node.workspace_id = candidate.workspace_id
                      AND node.id = candidate.node_id;
                END $$;
                """);

            migrationBuilder.CreateTable(
                name: "topology_edges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_handle = table.Column<string>(type: "text", nullable: false),
                    target_handle = table.Column<string>(type: "text", nullable: false),
                    edge_type = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topology_edges", x => x.id);
                    table.ForeignKey(
                        name: "FK_topology_edges_topology_nodes_workspace_id_source_node_id",
                        columns: x => new { x.workspace_id, x.source_node_id },
                        principalTable: "topology_nodes",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_topology_edges_topology_nodes_workspace_id_target_node_id",
                        columns: x => new { x.workspace_id, x.target_node_id },
                        principalTable: "topology_nodes",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_topology_edges_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM app_dependencies
                        GROUP BY workspace_id, source_app_id, dest_app_id, dest_port_id
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot add canonical dependency index: duplicate dependency rows exist';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_workspace_id_source_app_id_dest_app_id_des~",
                table: "app_dependencies",
                columns: new[] { "workspace_id", "source_app_id", "dest_app_id", "dest_port_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topology_edges_workspace_id_source_node_id_target_node_id_s~",
                table: "topology_edges",
                columns: new[] { "workspace_id", "source_node_id", "target_node_id", "source_handle", "target_handle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topology_edges_workspace_id_target_node_id",
                table: "topology_edges",
                columns: new[] { "workspace_id", "target_node_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "topology_edges");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_workspace_id_source_app_id_dest_app_id_des~",
                table: "app_dependencies");

        }
    }
}

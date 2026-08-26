using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations;

/// <summary>
/// Restores the legacy baseline that predates the first migration retained in this repository.
/// It creates tables only for an empty database, no-ops for a complete existing installation,
/// and fails closed for partial/manual schemas that require explicit operator adoption.
/// </summary>
[DbContext(typeof(AuditDbContext))]
[Migration("20260723000000_InitialLegacySchema")]
public class InitialLegacySchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        """
        DO $$
        DECLARE
            existing_count integer;
        BEGIN
            SELECT count(*) INTO existing_count
            FROM unnest(ARRAY[
                'applications', 'datacenters', 'workspaces', 'servers',
                'topology_nodes', 'port_mappings', 'app_dependencies'
            ]) AS required_table(name)
            WHERE to_regclass('public.' || required_table.name) IS NOT NULL;

            IF existing_count = 7 THEN
                -- Existing installations receive only a history stamp for this recovered baseline.
                RETURN;
            ELSIF existing_count <> 0 THEN
                RAISE EXCEPTION 'Partial legacy schema detected (% of 7 base tables). Run the migration preflight/adoption runbook.', existing_count;
            END IF;

            CREATE TABLE auditnode_schema_baseline_provenance (
                migration_id varchar(150) PRIMARY KEY,
                created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE applications (
                id uuid PRIMARY KEY,
                app_code text NOT NULL,
                app_name text NOT NULL,
                owner_team varchar(255) NOT NULL,
                tech_stack text NOT NULL,
                risk text NOT NULL,
                icon text NOT NULL,
                workspace_id uuid NOT NULL
            );

            CREATE TABLE datacenters (
                id uuid PRIMARY KEY,
                name text NOT NULL,
                location text NOT NULL
            );

            CREATE TABLE workspaces (
                id uuid PRIMARY KEY,
                name text NOT NULL,
                description text NOT NULL
            );

            CREATE TABLE servers (
                id uuid PRIMARY KEY,
                hostname text NOT NULL,
                ip_address text NOT NULL,
                os_type text NOT NULL,
                environment text NOT NULL,
                status text NOT NULL,
                datacenter_id uuid NOT NULL,
                workspace_id uuid NOT NULL,
                CONSTRAINT "FK_servers_datacenters_datacenter_id"
                    FOREIGN KEY (datacenter_id) REFERENCES datacenters(id) ON DELETE CASCADE
            );

            CREATE TABLE topology_nodes (
                id uuid PRIMARY KEY,
                node_type text NOT NULL,
                label text NOT NULL,
                x double precision NOT NULL,
                y double precision NOT NULL,
                width double precision NULL,
                height double precision NULL,
                parent_node_id uuid NULL,
                reference_id uuid NULL,
                workspace_id uuid NOT NULL,
                CONSTRAINT "FK_topology_nodes_topology_nodes_parent_node_id"
                    FOREIGN KEY (parent_node_id) REFERENCES topology_nodes(id) ON DELETE CASCADE
            );

            CREATE TABLE port_mappings (
                id uuid PRIMARY KEY,
                server_id uuid NOT NULL,
                app_id uuid NOT NULL,
                port_number integer NOT NULL,
                protocol text NOT NULL,
                workspace_id uuid NOT NULL,
                CONSTRAINT "FK_port_mappings_applications_app_id"
                    FOREIGN KEY (app_id) REFERENCES applications(id) ON DELETE CASCADE,
                CONSTRAINT "FK_port_mappings_servers_server_id"
                    FOREIGN KEY (server_id) REFERENCES servers(id) ON DELETE CASCADE
            );

            CREATE TABLE app_dependencies (
                id uuid PRIMARY KEY,
                source_app_id uuid NOT NULL,
                dest_app_id uuid NOT NULL,
                dest_port_id uuid NOT NULL,
                connection_type text NOT NULL,
                created_at timestamp with time zone NOT NULL,
                workspace_id uuid NOT NULL,
                CONSTRAINT "FK_app_dependencies_applications_dest_app_id"
                    FOREIGN KEY (dest_app_id) REFERENCES applications(id) ON DELETE CASCADE,
                CONSTRAINT "FK_app_dependencies_applications_source_app_id"
                    FOREIGN KEY (source_app_id) REFERENCES applications(id) ON DELETE CASCADE,
                CONSTRAINT "FK_app_dependencies_port_mappings_dest_port_id"
                    FOREIGN KEY (dest_port_id) REFERENCES port_mappings(id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX "IX_applications_app_code" ON applications(app_code);
            CREATE INDEX "IX_servers_datacenter_id" ON servers(datacenter_id);
            CREATE UNIQUE INDEX "IX_servers_ip_address" ON servers(ip_address);
            CREATE INDEX "IX_topology_nodes_parent_node_id" ON topology_nodes(parent_node_id);
            CREATE INDEX "IX_port_mappings_app_id" ON port_mappings(app_id);
            CREATE INDEX "IX_port_mappings_server_id" ON port_mappings(server_id);
            CREATE INDEX "IX_app_dependencies_dest_app_id" ON app_dependencies(dest_app_id);
            CREATE INDEX "IX_app_dependencies_dest_port_id" ON app_dependencies(dest_port_id);
            CREATE INDEX "IX_app_dependencies_source_app_id" ON app_dependencies(source_app_id);

            INSERT INTO auditnode_schema_baseline_provenance (migration_id)
            VALUES ('20260723000000_InitialLegacySchema');
        END $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        """
        DO $$
        BEGIN
            IF to_regclass('public.auditnode_schema_baseline_provenance') IS NULL OR NOT EXISTS (
                SELECT 1 FROM auditnode_schema_baseline_provenance
                WHERE migration_id = '20260723000000_InitialLegacySchema'
            ) THEN
                -- This migration only stamped an existing installation; it owns no base tables.
                RETURN;
            END IF;

            DROP TABLE app_dependencies;
            DROP TABLE topology_nodes;
            DROP TABLE port_mappings;
            DROP TABLE applications;
            DROP TABLE servers;
            DROP TABLE workspaces;
            DROP TABLE datacenters;
            DROP TABLE auditnode_schema_baseline_provenance;
        END $$;
        """);
}

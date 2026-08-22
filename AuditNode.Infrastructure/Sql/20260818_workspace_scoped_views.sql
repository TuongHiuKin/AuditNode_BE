-- Apply only after the workspace authorization migration has completed successfully.
-- This script is intentionally separate from EF migrations because both objects are read-only views.

BEGIN;

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

COMMIT;

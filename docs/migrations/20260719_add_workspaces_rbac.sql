-- AuditNode RBAC/workspace expand migration.
-- Review and back up the database before applying. This script deliberately leaves
-- legacy workspace_id columns nullable until the verification queries at the end
-- return zero unresolved rows.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS workspaces (
    id uuid PRIMARY KEY,
    name varchar(160) NOT NULL,
    owner_user_id varchar(100) NOT NULL,
    is_personal boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_workspaces_personal_owner
    ON workspaces (owner_user_id)
    WHERE is_personal = true;

CREATE TABLE IF NOT EXISTS workspace_members (
    workspace_id uuid NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    user_id varchar(100) NOT NULL,
    role varchar(40) NOT NULL,
    invited_by_user_id varchar(100) NOT NULL,
    joined_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (workspace_id, user_id),
    CONSTRAINT ck_workspace_members_role CHECK (
        role IN ('workspace_admin', 'editor', 'auditor', 'viewer')
    )
);
CREATE INDEX IF NOT EXISTS ix_workspace_members_user_id
    ON workspace_members (user_id);

ALTER TABLE datacenters ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE servers ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE applications ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE labels ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE port_mappings ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE app_dependencies ADD COLUMN IF NOT EXISTS workspace_id uuid;
-- ALTER TABLE IF EXISTS topology_nodes ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE boundary_frames ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE boundary_frames
    ALTER COLUMN owner_id TYPE varchar(100) USING owner_id::text;

WITH owners AS (
    SELECT owner_id::text AS owner_id FROM datacenters
    UNION SELECT owner_id::text FROM servers
    UNION SELECT owner_id::text FROM applications
    UNION SELECT owner_id::text FROM boundary_frames
)
INSERT INTO workspaces (id, name, owner_user_id, is_personal)
SELECT gen_random_uuid(), 'Personal workspace', owner_id, true
FROM owners
WHERE owner_id IS NOT NULL AND btrim(owner_id) <> ''
ON CONFLICT DO NOTHING;

UPDATE datacenters entity
SET workspace_id = workspace.id
FROM workspaces workspace
WHERE entity.workspace_id IS NULL
  AND workspace.owner_user_id = entity.owner_id::text
  AND workspace.is_personal = true;

UPDATE servers entity
SET workspace_id = workspace.id
FROM workspaces workspace
WHERE entity.workspace_id IS NULL
  AND workspace.owner_user_id = entity.owner_id::text
  AND workspace.is_personal = true;

UPDATE applications entity
SET workspace_id = workspace.id
FROM workspaces workspace
WHERE entity.workspace_id IS NULL
  AND workspace.owner_user_id = entity.owner_id::text
  AND workspace.is_personal = true;

-- UPDATE labels entity
-- SET workspace_id = workspace.id
-- FROM workspaces workspace
-- WHERE entity.workspace_id IS NULL
--   AND workspace.owner_user_id = entity.owner_id::text
--   AND workspace.is_personal = true;

UPDATE boundary_frames entity
SET workspace_id = workspace.id
FROM workspaces workspace
WHERE entity.workspace_id IS NULL
  AND workspace.owner_user_id = entity.owner_id::text
  AND workspace.is_personal = true;

UPDATE port_mappings mapping
SET workspace_id = server.workspace_id
FROM servers server, applications application
WHERE mapping.workspace_id IS NULL
  AND mapping.server_id = server.id
  AND mapping.app_id = application.id
  AND server.workspace_id = application.workspace_id;

UPDATE app_dependencies dependency
SET workspace_id = source.workspace_id
FROM applications source, applications destination, port_mappings port
WHERE dependency.workspace_id IS NULL
  AND dependency.source_app_id = source.id
  AND dependency.dest_app_id = destination.id
  AND dependency.dest_port_id = port.id
  AND source.workspace_id = destination.workspace_id
  AND source.workspace_id = port.workspace_id;

-- UPDATE topology_nodes node
-- SET workspace_id = server.workspace_id
-- FROM servers server
-- WHERE node.workspace_id IS NULL
--   AND server.id = node.reference_id;

-- UPDATE topology_nodes node
-- SET workspace_id = application.workspace_id
-- FROM applications application
-- WHERE node.workspace_id IS NULL
--   AND application.id = node.reference_id;

CREATE TABLE IF NOT EXISTS shared_label_frames (
    id uuid PRIMARY KEY,
    workspace_id uuid NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    label_id uuid NOT NULL REFERENCES labels(id) ON DELETE CASCADE,
    datacenter_id uuid NULL REFERENCES datacenters(id) ON DELETE SET NULL,
    environment varchar(80) NULL,
    token_hash varchar(64) NOT NULL,
    detail_preset varchar(40) NOT NULL,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    created_by_user_id varchar(100) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_shared_label_frames_detail CHECK (
        detail_preset IN ('safe', 'network_details')
    )
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_shared_label_frames_token_hash
    ON shared_label_frames (token_hash);

CREATE TABLE IF NOT EXISTS audit_logs (
    id uuid PRIMARY KEY,
    workspace_id uuid NULL REFERENCES workspaces(id) ON DELETE SET NULL,
    actor_user_id varchar(100) NOT NULL,
    action varchar(120) NOT NULL,
    resource_type varchar(80) NOT NULL,
    resource_id varchar(120) NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ip_address varchar(80) NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_audit_logs_workspace_created_at
    ON audit_logs (workspace_id, created_at DESC);

DROP INDEX IF EXISTS "IX_servers_ip_address";
DROP INDEX IF EXISTS ix_servers_ip_address;
DROP INDEX IF EXISTS "IX_applications_app_code";
DROP INDEX IF EXISTS ix_applications_app_code;

CREATE UNIQUE INDEX IF NOT EXISTS ux_servers_workspace_ip
    ON servers (workspace_id, ip_address);
CREATE UNIQUE INDEX IF NOT EXISTS ux_applications_workspace_code
    ON applications (workspace_id, app_code);
CREATE UNIQUE INDEX IF NOT EXISTS ux_port_mappings_workspace_server_port
    ON port_mappings (workspace_id, server_id, port_number);
CREATE INDEX IF NOT EXISTS ix_labels_workspace_key_value
    ON labels (workspace_id, key, value);
CREATE INDEX IF NOT EXISTS ix_app_dependencies_workspace
    ON app_dependencies (workspace_id);
-- CREATE INDEX topology_nodes
CREATE INDEX IF NOT EXISTS ix_boundary_frames_workspace
    ON boundary_frames (workspace_id);

COMMIT;

-- Verification: every count must be zero before a later contract migration
-- changes workspace_id to NOT NULL.
SELECT 'datacenters' AS table_name, count(*) AS unresolved
FROM datacenters WHERE workspace_id IS NULL
UNION ALL SELECT 'servers', count(*) FROM servers WHERE workspace_id IS NULL
UNION ALL SELECT 'applications', count(*) FROM applications WHERE workspace_id IS NULL
UNION ALL SELECT 'labels', count(*) FROM labels WHERE workspace_id IS NULL
UNION ALL SELECT 'port_mappings', count(*) FROM port_mappings WHERE workspace_id IS NULL
UNION ALL SELECT 'app_dependencies', count(*) FROM app_dependencies WHERE workspace_id IS NULL
-- UNION topology_nodes
UNION ALL SELECT 'boundary_frames', count(*) FROM boundary_frames WHERE workspace_id IS NULL;

-- Controlled, idempotent RBAC scope backfill.
-- Usage: psql "$CONNECTION_STRING" --set=ON_ERROR_STOP=1 -f this-file.sql < approved-rbac-scope.csv
-- CSV columns (one row per target): workspace_id,user_id,role,scope_mode,target_id,approved_by
-- For scope_mode=all, target_id must be empty. The script rejects unapproved, duplicate,
-- inconsistent, missing-member, and cross-workspace targets before changing any row.
BEGIN;

CREATE TEMP TABLE approved_rbac_scope_mapping (
    workspace_id uuid NOT NULL,
    user_id varchar(100) NOT NULL,
    role varchar(40) NOT NULL,
    scope_mode varchar(20) NOT NULL,
    target_id uuid NULL,
    approved_by varchar(100) NOT NULL
) ON COMMIT DROP;

\copy approved_rbac_scope_mapping (workspace_id, user_id, role, scope_mode, target_id, approved_by) FROM pstdin WITH (FORMAT csv, HEADER true, NULL '')

LOCK TABLE workspace_members,
           workspace_member_scopes,
           labels,
           topology_nodes,
           workspace_member_rbac_provenance
IN SHARE ROW EXCLUSIVE MODE;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM approved_rbac_scope_mapping) THEN
        RAISE EXCEPTION 'Approved mapping is empty';
    END IF;

    IF EXISTS (SELECT 1 FROM approved_rbac_scope_mapping WHERE btrim(approved_by) = '') THEN
        RAISE EXCEPTION 'Every mapping row requires approved_by';
    END IF;

    IF EXISTS (SELECT 1 FROM approved_rbac_scope_mapping WHERE role NOT IN ('workspace_admin', 'auditor', 'viewer')) THEN
        RAISE EXCEPTION 'Unsupported workspace role in mapping';
    END IF;

    IF EXISTS (SELECT 1 FROM approved_rbac_scope_mapping WHERE scope_mode NOT IN ('all', 'labels', 'frames')) THEN
        RAISE EXCEPTION 'Unsupported scope mode in mapping';
    END IF;

    IF EXISTS (
        SELECT 1 FROM approved_rbac_scope_mapping
        WHERE (scope_mode = 'all' AND target_id IS NOT NULL)
           OR (scope_mode <> 'all' AND target_id IS NULL)
           OR (role = 'workspace_admin' AND scope_mode <> 'all')
    ) THEN
        RAISE EXCEPTION 'Role/scope/target combination is invalid';
    END IF;

    IF EXISTS (
        SELECT workspace_id, user_id
        FROM approved_rbac_scope_mapping
        GROUP BY workspace_id, user_id
        HAVING count(DISTINCT (role, scope_mode)) <> 1
    ) THEN
        RAISE EXCEPTION 'A member has inconsistent role or scope rows';
    END IF;

    IF EXISTS (
        SELECT workspace_id, user_id, scope_mode, target_id
        FROM approved_rbac_scope_mapping
        GROUP BY workspace_id, user_id, scope_mode, target_id
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Duplicate scope target in mapping';
    END IF;

    IF EXISTS (
        SELECT workspace_id, user_id
        FROM approved_rbac_scope_mapping
        GROUP BY workspace_id, user_id
        HAVING count(DISTINCT approved_by) <> 1
    ) THEN
        RAISE EXCEPTION 'A member has multiple approvers in one mapping';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM approved_rbac_scope_mapping mapping
        LEFT JOIN workspace_members member
          ON member.workspace_id = mapping.workspace_id AND member.user_id = mapping.user_id
        WHERE member.user_id IS NULL
    ) THEN
        RAISE EXCEPTION 'Mapping references a missing workspace member';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM approved_rbac_scope_mapping mapping
        LEFT JOIN workspace_member_rbac_provenance provenance
          ON provenance.workspace_id = mapping.workspace_id AND provenance.user_id = mapping.user_id
        WHERE provenance.user_id IS NULL
    ) THEN
        RAISE EXCEPTION 'Mapping references a member without legacy role provenance';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM approved_rbac_scope_mapping mapping
        JOIN workspace_member_rbac_provenance provenance
          ON provenance.workspace_id = mapping.workspace_id AND provenance.user_id = mapping.user_id
        WHERE provenance.requires_manual_decision OR provenance.original_role IS NULL
    ) THEN
        RAISE EXCEPTION 'Mapping references a member with unresolved legacy role provenance';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM approved_rbac_scope_mapping mapping
        LEFT JOIN labels label
          ON label.workspace_id = mapping.workspace_id AND label.id = mapping.target_id
        WHERE mapping.scope_mode = 'labels' AND label.id IS NULL
    ) THEN
        RAISE EXCEPTION 'Label target is missing or belongs to another workspace';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM approved_rbac_scope_mapping mapping
        LEFT JOIN topology_nodes frame
          ON frame.workspace_id = mapping.workspace_id
         AND frame.id = mapping.target_id
         AND lower(frame.node_type) = 'frame'
        WHERE mapping.scope_mode = 'frames' AND frame.id IS NULL
    ) THEN
        RAISE EXCEPTION 'Frame target is missing or belongs to another workspace';
    END IF;
END $$;

CREATE TEMP TABLE desired_rbac_members ON COMMIT DROP AS
SELECT
    workspace_id,
    user_id,
    min(role) AS role,
    min(scope_mode) AS scope_mode,
    min(approved_by) AS approved_by,
    coalesce(array_agg(target_id ORDER BY target_id) FILTER (WHERE target_id IS NOT NULL), ARRAY[]::uuid[]) AS target_ids
FROM approved_rbac_scope_mapping
GROUP BY workspace_id, user_id;

CREATE TEMP TABLE changed_rbac_members ON COMMIT DROP AS
SELECT desired.workspace_id, desired.user_id
FROM desired_rbac_members desired
JOIN workspace_members member
  ON member.workspace_id = desired.workspace_id AND member.user_id = desired.user_id
LEFT JOIN LATERAL (
    SELECT
        coalesce(array_agg(scope.target_id ORDER BY scope.target_id), ARRAY[]::uuid[]) AS target_ids,
        coalesce(bool_or(scope.scope_type <> CASE desired.scope_mode
            WHEN 'labels' THEN 'label'
            WHEN 'frames' THEN 'frame'
            ELSE scope.scope_type
        END), false) AS has_wrong_scope_type
    FROM workspace_member_scopes scope
    WHERE scope.workspace_id = desired.workspace_id AND scope.user_id = desired.user_id
) current_scope ON true
WHERE member.role IS DISTINCT FROM desired.role
   OR member.scope_mode IS DISTINCT FROM desired.scope_mode
   OR current_scope.target_ids IS DISTINCT FROM desired.target_ids
   OR current_scope.has_wrong_scope_type;

UPDATE workspace_members member
SET role = desired.role,
    scope_mode = desired.scope_mode,
    version = member.version + 1
FROM desired_rbac_members desired
JOIN changed_rbac_members changed
  ON changed.workspace_id = desired.workspace_id AND changed.user_id = desired.user_id
WHERE member.workspace_id = desired.workspace_id AND member.user_id = desired.user_id;

DELETE FROM workspace_member_scopes scope
USING desired_rbac_members desired
WHERE scope.workspace_id = desired.workspace_id
  AND scope.user_id = desired.user_id
  AND (
      desired.scope_mode = 'all'
      OR scope.scope_type <> CASE desired.scope_mode WHEN 'labels' THEN 'label' ELSE 'frame' END
      OR NOT (scope.target_id = ANY(desired.target_ids))
  );

INSERT INTO workspace_member_scopes
    (id, workspace_id, user_id, scope_type, target_id, created_at, created_by_user_id)
SELECT
    gen_random_uuid(),
    desired.workspace_id,
    desired.user_id,
    CASE desired.scope_mode WHEN 'labels' THEN 'label' ELSE 'frame' END,
    target.target_id,
    CURRENT_TIMESTAMP,
    desired.approved_by
FROM desired_rbac_members desired
CROSS JOIN LATERAL unnest(desired.target_ids) AS target(target_id)
WHERE desired.scope_mode <> 'all'
ON CONFLICT (workspace_id, user_id, scope_type, target_id) DO NOTHING;

INSERT INTO workspace_member_scope_backfill_audit
    (workspace_id, user_id, approved_role, approved_scope_mode, approved_target_ids,
     approved_by_user_id, approved_at)
SELECT
    workspace_id,
    user_id,
    role,
    scope_mode,
    target_ids,
    approved_by,
    CURRENT_TIMESTAMP
FROM desired_rbac_members
ON CONFLICT (workspace_id, user_id) DO UPDATE
SET approved_role = EXCLUDED.approved_role,
    approved_scope_mode = EXCLUDED.approved_scope_mode,
    approved_target_ids = EXCLUDED.approved_target_ids,
    approved_by_user_id = EXCLUDED.approved_by_user_id,
    approved_at = EXCLUDED.approved_at
WHERE workspace_member_scope_backfill_audit.approved_role IS DISTINCT FROM EXCLUDED.approved_role
   OR workspace_member_scope_backfill_audit.approved_scope_mode IS DISTINCT FROM EXCLUDED.approved_scope_mode
   OR workspace_member_scope_backfill_audit.approved_target_ids IS DISTINCT FROM EXCLUDED.approved_target_ids
   OR workspace_member_scope_backfill_audit.approved_by_user_id IS DISTINCT FROM EXCLUDED.approved_by_user_id;

COMMIT;

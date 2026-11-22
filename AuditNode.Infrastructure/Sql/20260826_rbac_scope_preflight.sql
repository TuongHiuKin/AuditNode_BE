-- Read-only inventory for operator review before or after RbacScopedSharing.
-- This is a psql control script. It performs only SELECT statements against persistent data.
BEGIN TRANSACTION READ ONLY;

SELECT to_regclass('public.workspace_members') IS NOT NULL
   AS workspace_members_exists \gset

SELECT EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'workspace_members'
      AND column_name = 'scope_mode'
) AS rbac_schema_exists \gset

SELECT to_regclass('public.workspace_member_rbac_provenance') IS NOT NULL
   AND to_regclass('public.workspace_member_scope_backfill_audit') IS NOT NULL
   AS rbac_support_exists \gset

\if :workspace_members_exists
\if :rbac_schema_exists
    \if :rbac_support_exists
        SELECT
            member.workspace_id AS "WorkspaceId",
            member.user_id AS "UserId",
            provenance.original_role AS "OriginalRole",
            member.role AS "ProposedRole",
            member.scope_mode AS "ProposedScopeMode",
            coalesce(array_agg(scope.target_id ORDER BY scope.target_id)
                FILTER (WHERE scope.target_id IS NOT NULL), ARRAY[]::uuid[]) AS "TargetIds",
            coalesce(provenance.requires_manual_decision, false)
                OR (member.scope_mode <> 'all' AND count(scope.target_id) = 0)
                OR (provenance.user_id IS NOT NULL
                    AND member.role <> 'workspace_admin'
                    AND audit.user_id IS NULL)
                AS "RequiresManualDecision"
        FROM workspace_members member
        LEFT JOIN workspace_member_scopes scope
          ON scope.workspace_id = member.workspace_id AND scope.user_id = member.user_id
        LEFT JOIN workspace_member_rbac_provenance provenance
          ON provenance.workspace_id = member.workspace_id AND provenance.user_id = member.user_id
        LEFT JOIN workspace_member_scope_backfill_audit audit
          ON audit.workspace_id = member.workspace_id AND audit.user_id = member.user_id
        GROUP BY member.workspace_id, member.user_id, provenance.original_role,
                 provenance.user_id, provenance.requires_manual_decision, audit.user_id,
                 member.role, member.scope_mode
        ORDER BY member.workspace_id, member.user_id;
    \else
        -- Older RBAC installations do not contain enough evidence to distinguish a
        -- native auditor from a converted editor. Report that origin as unresolved.
        SELECT
            member.workspace_id AS "WorkspaceId",
            member.user_id AS "UserId",
            CASE WHEN member.role = 'auditor' THEN NULL ELSE member.role END AS "OriginalRole",
            member.role AS "ProposedRole",
            member.scope_mode AS "ProposedScopeMode",
            coalesce(array_agg(scope.target_id ORDER BY scope.target_id)
                FILTER (WHERE scope.target_id IS NOT NULL), ARRAY[]::uuid[]) AS "TargetIds",
            member.role = 'auditor'
                OR (member.scope_mode <> 'all' AND count(scope.target_id) = 0)
                OR member.role <> 'workspace_admin'
                AS "RequiresManualDecision"
        FROM workspace_members member
        LEFT JOIN workspace_member_scopes scope
          ON scope.workspace_id = member.workspace_id AND scope.user_id = member.user_id
        GROUP BY member.workspace_id, member.user_id, member.role, member.scope_mode
        ORDER BY member.workspace_id, member.user_id;
    \endif
\else
    SELECT
        member.workspace_id AS "WorkspaceId",
        member.user_id AS "UserId",
        member.role AS "OriginalRole",
        CASE WHEN member.role = 'editor' THEN 'auditor' ELSE member.role END AS "ProposedRole",
        CASE WHEN member.role = 'workspace_admin' THEN 'all' ELSE 'labels' END AS "ProposedScopeMode",
        ARRAY[]::uuid[] AS "TargetIds",
        member.role <> 'workspace_admin' AS "RequiresManualDecision"
    FROM workspace_members member
    ORDER BY member.workspace_id, member.user_id;
\endif
\else
    -- A pre-workspace-members installation has no legacy memberships to widen.
    SELECT
        NULL::uuid AS "WorkspaceId",
        NULL::varchar(100) AS "UserId",
        NULL::varchar(40) AS "OriginalRole",
        NULL::varchar(40) AS "ProposedRole",
        NULL::varchar(20) AS "ProposedScopeMode",
        ARRAY[]::uuid[] AS "TargetIds",
        NULL::boolean AS "RequiresManualDecision"
    WHERE false;
\endif

COMMIT;

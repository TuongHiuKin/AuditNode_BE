# RBAC scoped-sharing migration runbook

This runbook is for the operator-gated conversion of legacy workspace memberships. Do not grant `all` scope to legacy Viewer/Auditor/Editor rows by default.

## 1. Preflight (read-only)

Run `AuditNode.Infrastructure/Sql/20260826_rbac_scope_preflight.sql` against a backup/restored copy first, then production before the maintenance window. Export the rows as a non-secret review artifact.

Every row with `RequiresManualDecision=true` must be reviewed by the workspace owner or security reviewer. `editor` is proposed as `auditor`, but its target labels/frames remain empty until explicitly approved.

## 2. Apply schema migrations in gated steps

Take a database backup and record the current migration ID. On a legacy database, first migrate only through `WorkspaceAuthorizationConsistency`. That migration intentionally leaves ambiguous datacenter ownership as `NULL`; fill those rows and every `workspaces.owner_user_id`, then continue through `FinalizeWorkspaceOwnershipConstraints` and finally the latest migration. A fresh empty database can migrate directly to latest.

`RbacMigrationSafetySupport` records membership roles before `RbacScopedSharing` converts `editor` to `auditor`. If RBAC was already recorded before this remediation is deployed, existing auditors are recorded with `original_role=NULL` and `requires_manual_decision=true`; this is deliberate because the old data cannot prove whether each auditor was native or converted.

The same support migration repairs a retained schema drift by making `workspaces.description` nullable, matching the current domain model. Its rollback refuses to restore `NOT NULL` while any workspace still has a null description.

## 3. Controlled backfill

Prepare an approved CSV with columns:

```text
workspace_id,user_id,role,scope_mode,target_id,approved_by
```

Use one row per label/frame target. Use one row with an empty `target_id` for `all`. Feed the local approved CSV through standard input while psql reads the control script with `--file`:

```text
psql "$CONNECTION_STRING" --set=ON_ERROR_STOP=1 --file=AuditNode.Infrastructure/Sql/20260826_rbac_scope_backfill.sql < approved-rbac-scope.csv
```

`FROM pstdin` is intentional: the database server never needs filesystem access to the operator's review artifact.

The script locks membership, scope, label, topology-node, and provenance writes for the transaction and rejects empty files, missing or mixed approval, inconsistent/duplicate mappings (including duplicate `all` rows), missing/unresolved role provenance, missing members, and targets from another workspace. It replaces both stale target IDs and stale scope types, and persists the approved role/scope/targets even for `all`. Re-running the same mapping is idempotent and does not increment member versions again.

## 4. Verify

Re-run the preflight/report, inspect `workspace_members` and `workspace_member_scopes`, then execute the RBAC E2E contracts. Confirm that unapproved scoped members remain fail-closed.

## 5. Rollback

Rollback the EF migration only during the approved maintenance window. Resolve every provenance row marked `requires_manual_decision` first: set `original_role='editor'` only when the review artifact proves it was converted, otherwise set `original_role='auditor'`; record reviewer, timestamp, artifact reference/hash, and clear the flag. RBAC rollback aborts while any unresolved provenance remains, restores `editor` only for proven legacy editors, and leaves native/new auditors unchanged. Restore the database backup if later application writes make a schema rollback unsafe.

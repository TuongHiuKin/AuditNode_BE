# Database Documentation - PostgreSQL & EF Core

## Current Global Catalog schema and rollout status (September 2026)

All mutable catalog data is partitioned by required `owner_user_id` values. The current model has no Workspace runtime tables or `workspace_id` columns. Owner-scoped alternate keys and composite foreign keys prevent datacenters, servers, applications, deployments, labels, topology, grants, and dependencies from crossing owner catalogs. Server IP, application code, label key/value, deployment port, topology edge, and dependency uniqueness are enforced within the relevant owner catalog.

Application and server label joins carry `owner_user_id` and reference owner-consistent resource/label keys. `label_grants` supports either a direct `grantee_user_id` or a hashed anonymous token, Viewer/Editor permissions, expiry/revocation, and an optimistic-concurrency version; database checks constrain anonymous tokens to Viewer and require their expiry. `owner_catalog_states` holds the per-owner topology revision. The `v_topology_map` and `v_dependency_graph` views project `owner_user_id` and join rows only when ownership matches.

### Current owner-scoped cutover migrations

- `20260831161239_RemoveWorkspaceRuntime` fails closed unless the legacy Workspace and catalog tables are empty, then drops Workspace tables/columns, makes catalog ownership required, and recreates the read views with `owner_user_id`.
- `20260901045717_EnforceOwnerScopedRelations` adds owner/id alternate keys plus owner-consistent indexes and foreign keys across the catalog graph.
- `20260901130248_RemoveLegacyWorkspaceMigrationArtifacts` removes the remaining Workspace migration-audit tables.

The repository cannot establish whether these migrations have been applied to any deployed database. Inspect that environment's `__EFMigrationsHistory` before rollout. `RemoveWorkspaceRuntime` is intentionally a destructive reset boundary and is not an in-place data-preserving upgrade for a populated legacy Workspace database; preserve a verified backup and use only an explicitly approved reset/migration procedure.

### Legacy Workspace remediation snapshot (superseded)

The following notes document the earlier Workspace-era rollout and are retained for historical audit context. They do not describe the current runtime schema or current authorization model.

The following artifacts were generated and reviewed, but **have not been applied to a live database**:

1. `AuditNode.Infrastructure/Migrations/20260818123754_WorkspaceAuthorizationConsistency.cs` — adds workspace membership/owner metadata, datacenter tenancy, same-workspace relationships and indexes, and workspace-consistent model changes. Existing owner and unassigned datacenter fields remain nullable for an explicit staged backfill.
2. `AuditNode.Infrastructure/Sql/20260818_workspace_scoped_views.sql` — replaces `v_topology_map` and `v_dependency_graph` so each projects `workspace_id` and joins tenant-consistently.
3. `AuditNode.Infrastructure/Migrations/20260818151536_ApplicationLabelsAndDeploymentContracts.cs` — creates the workspace-scoped application-label join and supporting label key/indexes.
4. `AuditNode.Infrastructure/Migrations/20260818152511_TopologyCanonicalState.cs` — creates workspace-scoped topology edges and enforces dependency uniqueness.

These are the three new EF migrations plus one SQL view artifact. The older `docs/migrations/20260719_add_workspaces_rbac.sql` is a legacy/preparatory script and is not a substitute for reviewing the current migration chain.

#### Historical Workspace apply runbook

1. Take and verify a restorable database backup. Record the current EF migration history and capture counts for workspaces, datacenters, servers, applications, deployments, labels, and dependencies.
2. Audit/backfill ownership and tenant data before enforcing final constraints. The first migration does not fabricate ownership from optional legacy columns: `owner_user_id` remains nullable, and datacenter workspace is populated only when its servers identify exactly one non-empty workspace. Unassigned rows remain nullable for an operator-approved backfill. Until a workspace receives a real owner or membership row, application authorization intentionally grants no access to it.
3. Detect and resolve duplicate dependency rows before the topology migration. Its precondition raises an error; it does not silently delete data. Also confirm tenant duplicates will not violate the new server-IP, application-code, or server-port indexes.
4. Apply EF migrations in timestamp order: workspace consistency, application labels/deployment contracts, then canonical topology state. Do not reorder or cherry-pick them.
5. Apply `20260818_workspace_scoped_views.sql` after the workspace columns/constraints exist and before serving topology/dependency reads. Verify both views expose only the requested workspace.
6. Run smoke reads/writes under at least two workspaces, verify membership denial/cross-workspace references, then run the backend test/build/model checks documented in the README.

Rollback can be lossy: reversing the workspace migration may remove tenant ownership/membership or restored constraints; reversing application labels/topology edges drops newly persisted join/edge data; replacing views can break readers expecting `workspace_id`. Prefer restore-to-backup for a failed production rollout. If a down migration is used, first export new label/edge/member data and confirm no application instance is using the new contracts.

### Schema verification commands

From the backend directory:

```powershell
dotnet ef migrations list --project AuditNode.Infrastructure/AuditNode.Infrastructure.csproj --startup-project AuditNode.API/AuditNode.API.csproj
dotnet ef migrations has-pending-model-changes --project AuditNode.Infrastructure/AuditNode.Infrastructure.csproj --startup-project AuditNode.API/AuditNode.API.csproj --no-build
```

The remediation baseline reported no pending model changes. These commands inspect the code/model; they do not prove a live database was migrated. Do not run `database update` until the backup, backfill, duplicate audit, and maintenance window are approved.

---

## Legacy schema snapshot (superseded)

The material below predates the Global Catalog owner cutover, Label Grants, application labels, and canonical topology edges. Use it only for historical context.

## Overview
The project uses **PostgreSQL** with **Entity Framework Core** for data persistence. All tables use `snake_case` naming conventions in the database and map to `PascalCase` properties in C# models.

---

## 1. Core Tables

### `servers`
Infrastructure nodes tracking physical or virtual machines.
- `id` (UUID, PK)
- `datacenter_id` (UUID)
- `ip_address` (VARCHAR)
- `hostname` (VARCHAR)
- `os_type` (VARCHAR)
- `environment` (VARCHAR)
- `status` (VARCHAR)

### `applications`
Software services registered in the system.
- `id` (UUID, PK)
- `app_code` (VARCHAR, UNIQUE) - *Enforced via Unique Index to support Upsert logic*
- `app_name` (VARCHAR)
- `owner_team` (VARCHAR) - *Identifies the team responsible for the app*
- `risk` (VARCHAR) - *Risk level (LOW, MEDIUM, HIGH)*
- `icon` (VARCHAR) - *Icon identifier for UI*
- `tech_stack` (VARCHAR) - *Primary technology stack*

### `port_mappings`
The critical bridge between servers and applications, allowing many-to-many relationship mapping.
- `id` (UUID, PK)
- `server_id` (FK → servers.id, Cascade Delete)
- `app_id` (FK → applications.id, Cascade Delete)
- `port_number` (INT)
- `protocol` (VARCHAR)

### `app_dependencies`
Logical connections between applications.
- `id` (UUID, PK)
- `source_app_id` (FK → applications.id)
- `dest_app_id` (FK → applications.id)
- `dest_port_id` (FK → port_mappings.id)
- `connection_type` (VARCHAR)
- `created_at` (TIMESTAMP)

---

## 2. Read-Only Views

### `v_topology_map` (TopologyView)
Aggregated view for G1 Inventory hierarchical tree.
- **`server_id`**: UUID of the server.
- **`server_hostname`**: Hostname of the server.
- **`server_ip`**: IP address.
- **`app_id`**: UUID of the application hosted.
- **`app_name`**: Name of the application.
- **`app_code`**: Unique application code.
- **`port_number`**: Port number the app is listening on.
- **`protocol`**: Protocol (TCP/UDP/HTTP).
- **`environment`**: Server environment (Dev/Staging/Prod).

### `v_dependency_graph` (DependencyView)
Aggregated view for G3 Topology graph visualization.
- **`source_app_id`**: Source application UUID.
- **`source_app_name`**: Source application name.
- **`source_app_code`**: Source application code.
- **`dest_app_id`**: Destination application UUID.
- **`dest_app_name`**: Destination application name.
- **`dest_app_code`**: Destination application code.
- **`dest_port_number`**: Port number on the destination server.
- **`connection_type`**: Type of connection (e.g., REST, SQL).
- **`dest_server_hostname`**: Hostname of the destination server.

---

## 3. EF Core Configuration
All entity relationships and view mappings are configured in `AuditDbContext.OnModelCreating()` in the **`AuditNode.Infrastructure`** project:
- **Fluent API**: Preferred over Data Annotations for complex mapping.
- **Keyless Entities**: `TopologyView` and `DependencyView` are mapped using `.HasNoKey().ToView()`.
- **Snake Case Mapping**: Explicitly mapped via `.HasColumnName("snake_case")` in `OnModelCreating`.
---

## 4. Cascading Purge Logic (Transactional Hard Delete)
To maintain referential integrity in PostgreSQL (Error 23503 prevention), the system implements a strict sequential deletion order when purging an application:
1. **`app_dependencies`**: Removes all connections where the application is either a `Source` or a `Destination` (matched via `DestAppId` or its entries in `port_mappings.id`).
2. **`port_mappings`**: Removes all infrastructure bindings for the application.
3. **`applications`**: Removes the root record.

All steps are wrapped in an `IDbContextTransaction` to ensure that partial deletions do not leave orphan records.

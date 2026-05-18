# Database Documentation - PostgreSQL & EF Core

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
- `app_code` (VARCHAR, UNIQUE)
- `app_name` (VARCHAR)
- `owner_id` (UUID) - *Maps to Keycloak User ID*

### `port_mappings`
Bridge between servers and applications.
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
- **Cascading Deletes**: Configured for `PortMapping` and `AppDependency` relationships to maintain referential integrity.

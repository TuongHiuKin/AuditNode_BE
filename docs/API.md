# AuditNode Backend API Documentation

## Canonical contract (August 2026)

All routes below require a valid bearer access token unless marked anonymous. Tenant routes also require `X-Workspace-Id`. Missing, malformed, or empty workspace IDs return 400; an unknown workspace returns 404; a user who is neither owner nor member receives 403. `/api/v1/auth/*` and `GET /api/v1/workspaces` do not require the workspace header.

### Authentication gateway

| Method | Route | Access | Result |
|---|---|---|---|
| POST | `/api/v1/auth/login` | Anonymous, rate limited | Returns access-token metadata and sets the refresh cookie |
| POST | `/api/v1/auth/register` | Anonymous, rate limited | Creates the Keycloak user and returns 201 |
| POST | `/api/v1/auth/refresh` | Anonymous, rate limited | Uses the refresh cookie and rotates the session |
| POST | `/api/v1/auth/logout` | Authenticated | Clears the local cookie and returns 204 |
| GET | `/api/v1/auth/me` | Authenticated | Returns the authenticated user projection |
| GET | `/api/v1/workspaces` | Authenticated | Returns owned/member workspaces |

The cookie is named `auditnode.refresh_token`, is HttpOnly and Secure, uses `SameSite=None`, and is scoped to `/api/v1/auth`. Required configuration keys are `Keycloak:Authority`, `Keycloak:Realm`, `Keycloak:Audience`, `Keycloak:AdminClientId`, `Keycloak:AdminClientSecret`, `Keycloak:BffClientId`, and `Keycloak:BffClientSecret`; this document intentionally contains no values.

### Servers

| Method | Route | Notes |
|---|---|---|
| GET | `/api/v1/servers` | Tenant-filtered list |
| GET | `/api/v1/servers/{id}` | Single server or 404 |
| POST | `/api/v1/servers` | Validates IPv4 and same-workspace datacenter; returns 201 |
| PUT | `/api/v1/servers/{id}` | Updates metadata including IP address; returns 204 |
| DELETE | `/api/v1/servers/{id}` | Permanent purge; returns 204 |
| GET | `/api/v1/servers/export?ids={id}&ids={id}` | JSON export; de-duplicates IDs |

Duplicate IP addresses in a workspace produce 409. Server responses/export include datacenter data, labels, and application deployments. Each deployed application includes the real `portMappingId` plus application identity, port, and protocol.

### Applications, deployments, and labels

| Method | Route | Notes |
|---|---|---|
| GET | `/api/v1/applications?labelKey={key}&labelValue={value}` | Tenant-filtered list and optional label filter |
| GET | `/api/v1/applications/{id}` | Application, labels, and deployments |
| POST | `/api/v1/applications` | Creates metadata, labels, and an optional initial deployment atomically |
| PUT | `/api/v1/applications/{id}` | Updates metadata; omitted `labels` preserves labels, `[]` clears them |
| GET | `/api/v1/applications/export?ids={id}&ids={id}` | JSON export; de-duplicates IDs |
| PUT | `/api/v1/infrastructure/apps/migrate` | Requires `portMappingId`, `targetServerId`, and `newPortNumber` |
| GET | `/api/v1/infrastructure/servers/{id}/deployed-apps` | Every item exposes `portMappingId` |
| GET | `/api/v1/infrastructure/apps/{id}/dependencies-count` | Dependency count before purge |
| DELETE | `/api/v1/infrastructure/apps/{id}/purge` | Permanent purge |

Application codes are unique per workspace. Deployment server references must exist in the same workspace, ports must be 1–65535, and a server-port collision returns 409. An application response exposes `labels` and `servers`; each server item contains `portMappingId`, server ID, hostname/IP, port, and protocol.

### Topology and dependencies

| Method | Route | Notes |
|---|---|---|
| GET | `/api/v1/topology/tree` | Supports datacenter, labels, `skip`, and `take`; invalid paging returns 400 |
| GET | `/api/v1/topology/map` | Workspace-scoped topology projection |
| GET | `/api/v1/topology/status` | Workspace-scoped status summary |
| GET | `/api/v1/topology/state` | Loads canonical `{nodes, edges}` state |
| POST/PUT | `/api/v1/topology/state` | Validates and saves the complete state; returns 204 |
| PUT | `/api/v1/dependencies/sync` | Idempotently synchronizes dependency references |

Topology application nodes use `PortMappingId` as stable node identity and also expose typed `AppId`, `ServerId`, and `PortMappingId`. Server nodes expose `ServerId`. A connection retains `SourceAppId`, `TargetAppId`, `DestinationPortMappingId`, and `DestinationServerId`. State nodes preserve node/frame type, parent, position, size, label, and reference; edges preserve endpoints, handles, type, label, and reference. Validation rejects empty IDs, missing or cross-workspace references, invalid parents, cycles, self-loops, duplicate identities, and destination mappings that do not belong to the destination application. Separate `/frames` and `/topology/sync` routes are not part of the canonical contract.

### Inventory import, export, and errors

| Method | Route | Notes |
|---|---|---|
| GET | `/api/v1/inventory/import-template` | Downloads the supported workbook template |
| POST | `/api/v1/inventory/import` | Accepts one `.xlsx` workbook up to 10 MB |

Imports validate OpenXML content, a worksheet, the exact eight headers (`Server Name`, `IP`, `Environment`, `App Code`, `App Name`, `Owner Team`, `Port`, `Protocol`), canonical IPv4, ports 1–65535, required fields, case-normalized application codes/protocols, case-insensitive duplicate rows/codes, existing conflicts, and tenant-visible datacenter/server references. Validation completes before a single transaction/save; failures roll back without partial commits.

Server and application exports are tenant-filtered JSON. They include finalized datacenter, labels, deployments, and `PortMappingId` data; spreadsheet export generation is not a backend contract.

Unexpected failures use safe Problem Details responses. The response includes a `correlationId` extension and an `X-Correlation-ID` header; exception details are logged server-side and are never returned through `ex.Message`. Expected validation/not-found/conflict failures remain 400/404/409 rather than becoming 500.

---

## Legacy endpoint snapshot (superseded)

The material below is retained only as a historical snapshot. Its unversioned routes, hard-coded local identity settings, and older DTO shapes are not the current API contract; use the canonical section above.

## Base URL
- **HTTPS:** `https://localhost:5001`
- **HTTP:** `http://localhost:5000`

## Authentication & Security
The API is secured using **Keycloak Identity Provider** via **JWT Bearer Authentication**.
- **Issuer/Authority:** `http://localhost:8080/realms/AuditNode-Realm`
- **Mechanism:** Bearer Token in `Authorization` header.
- **Requirement:** ALL endpoints (except OpenApi/Scalar) require a valid JWT token issued by the AuditNode-Realm.

## CORS Policy
**Policy Name:** `AllowReact`
- **Allowed Origins:** `http://localhost:5173`, `http://localhost:3000`
- **Methods:** All (GET, POST, etc.)

---

## 1. Servers Endpoints

### GET `/api/servers`
Retrieves all servers with bound applications.

**Response (200 OK):**
```json
[
  {
    "id": "uuid",
    "datacenterId": "uuid",
    "ipAddress": "string",
    "hostname": "string",
    "osType": "string",
    "environment": "string",
    "status": "string",
    "applications": [
      {
        "id": "uuid",
        "appCode": "string",
        "appName": "string",
        "ownerTeam": "string",
        "portNumber": 0,
        "protocol": "string"
      }
    ]
  }
]
```

### POST `/api/servers`
Registers a new server.

**Request Body:**
```json
{
  "datacenterId": "uuid",
  "ipAddress": "string",
  "hostname": "string",
  "osType": "string",
  "environment": "string",
  "status": "string"
}
```

### GET `/api/servers/{id}`
Retrieves detailed information about a single server by its ID, including hosted applications.

**Response (200 OK):**
```json
{
  "id": "uuid",
  "datacenterId": "uuid",
  "datacenterName": "string",
  "ipAddress": "string",
  "hostname": "string",
  "osType": "string",
  "environment": "string",
  "status": "string",
  "applications": [
    {
      "id": "uuid",
      "appCode": "string",
      "appName": "string",
      "ownerTeam": "string",
      "portNumber": 0,
      "protocol": "string"
    }
  ]
}
```

### PUT `/api/servers/{id}`
Updates an existing server's metadata. `IpAddress` is immutable.

**Request Body (`UpdateServerDto`):**
```json
{
  "hostname": "string",
  "osType": "string",
  "environment": "string",
  "status": "string",
  "datacenterId": "uuid"
}
```

**Response:**
- `204 No Content`: Update successful.
- `404 Not Found`: Server not found.

---

## 2. Applications Endpoints

### GET `/api/applications`
Retrieves all registered applications with their associated servers.

**Response (200 OK):**
```json
[
  {
    "id": "uuid",
    "appCode": "string",
    "appName": "string",
    "ownerTeam": "string",
    "risk": "string",
    "icon": "string",
    "techStack": "string",
    "servers": [
      {
        "id": "uuid",
        "hostname": "string",
        "ipAddress": "string",
        "portNumber": 0,
        "protocol": "string"
      }
    ]
  }
]
```

### POST `/api/applications`
Registers or updates an application (Find or Create / Upsert). Applications are now created independently of servers.

**Request Body:**
```json
{
  "appCode": "string",
  "appName": "string",
  "ownerTeam": "string",
  "risk": "string",
  "icon": "string",
  "techStack": "string"
}
```

### PUT `/api/applications/{id}`
Updates an existing application's metadata and its network residency (server and port). The `id` is passed in the URL, and the fields to be updated are provided in the body. `AppCode` is immutable and cannot be updated.

**Request Body (`UpdateApplicationDto`):**
```json
{
  "appName": "string",
  "ownerTeam": "string",
  "risk": "string",
  "icon": "string",
  "techStack": "string",
  "serverId": "uuid",
  "portNumber": 8080
}
```

**Response:**
- `204 No Content`: Update successful.
- `400 Bad Request`: Validation failed.
- `404 Not Found`: Application with the given ID does not exist.

---

## 2.1 Workspaces Endpoints

### GET `/api/v1/workspaces`
Retrieves all workspaces accessible to the authenticated user.

**Response (200 OK):**
```json
[
  {
    "id": "uuid",
    "name": "string",
    "description": "string"
  }
]
```

---

## 3. Datacenters Endpoints

### GET `/api/datacenters`
Retrieves a lightweight list of all datacenters (optimized for dropdowns).

**Response (200 OK):**
```json
[
  {
    "id": "uuid",
    "name": "string"
  }
]
```

### POST `/api/datacenters`
Registers a new datacenter.

**Request Body:**
```json
{
"name": "string",
"location": "string"
}
```

---

## 4. Dependencies Endpoints

### PUT `/api/dependencies/sync`
Declarative state synchronization for application dependencies. Calculates the delta (insertions/deletions) based on the provided list of edges.

**Request Body (`SyncDependenciesDto`):**
```json
{
  "dependencies": [
    {
      "sourceAppId": "uuid",
      "destAppId": "uuid"
    }
  ]
}
```

**Response (200 OK):**
```json
{
  "message": "Dependencies synchronized successfully."
}
```

---

## 5. Analytics Endpoints
### GET `/api/analytics/topology`
Retrieves topology data for tree view visualization. Joins servers, port mappings, and applications.

**Sample Response Item:**
```json
{
  "serverId": "uuid",
  "serverHostname": "string",
  "serverIp": "string",
  "appId": "uuid",
  "appName": "string",
  "appCode": "string",
  "portNumber": 80,
  "protocol": "HTTP",
  "environment": "Production"
}
```

### GET `/api/analytics/dependencies`
Retrieves application dependency data for graph visualization (React Flow).

**Sample Response Item:**
```json
{
  "sourceAppId": "uuid",
  "sourceAppName": "string",
  "sourceAppCode": "string",
  "destAppId": "uuid",
  "destAppName": "string",
  "destAppCode": "string",
  "destPortNumber": 443,
  "connectionType": "HTTPS",
  "destServerHostname": "string"
}
```

---

## 5. Inventory Import Endpoints

### GET `/api/inventory/import-template`
Downloads an Excel (.xlsx) template pre-configured for bulk inventory import. Includes data validation (dropdowns) for Environment and Protocol fields.

### POST `/api/inventory/import`
Processes a bulk import of servers and applications from an uploaded Excel file.

**Request:** `multipart/form-data` with a file field containing the `.xlsx` file.

**Response (200 OK):**
```json
{
  "totalProcessed": 10,
  "savedCount": 8,
  "errors": [
    {
      "row": 2,
      "type": "Validation",
      "message": "App Code is required."
    }
  ],
  "conflicts": [
    {
      "row": 5,
      "appCode": "APP01",
      "message": "AppCode APP01 already exists with a different name."
    }
  ]
}
```

---

## 6. Universal Search Endpoints

### GET `/api/search?keyword=...`
Performs a case-insensitive unified search across Servers (Hostname, IP) and Applications (AppCode, AppName).

**Query Parameters:**
- `keyword` (string, required): The search term. Must be at least 2 characters long.

**Response (200 OK):**
```json
[
  {
    "id": "uuid",
    "type": "SERVER",
    "title": "ProductionServer01",
    "subtitle": "IP: 10.0.0.1",
    "matchReason": "Matched by Server Hostname"
  },
  {
    "id": "uuid",
    "type": "APP",
    "title": "CustomerPortal",
    "subtitle": "On Server: Host01 (Port: 443)",
    "matchReason": "Matched by App Name"
  }
]
```

---

## 8. Infrastructure Endpoints

### GET `/api/infrastructure/apps/{id}/dependencies-count`
Retrieves the total count of inbound and outbound dependencies for a specific application. Useful for pre-check before deletion or migration.

**Response (200 OK):**
```json
5
```

### PUT `/api/infrastructure/apps/migrate`
Updates a port mapping, effectively migrating an application to a new target server or updating its port.

**Request Body (`MigrateAppDto`):**
```json
{
  "portMappingId": "uuid",
  "targetServerId": "uuid",
  "newPortNumber": 8080
}
```

**Response (200 OK):**
```json
{
  "message": "Migration successful"
}
```

### DELETE `/api/infrastructure/apps/{id}/purge`
Performs a safe, cascading hard delete of an application and all its associated dependencies and port mappings.

**Response (200 OK):**
```json
{
  "message": "Application and dependencies purged successfully"
}
```

---

## 9. Error Handling
Errors follow a consistent format:
```json
{
  "error": "Detailed error message"
}
```

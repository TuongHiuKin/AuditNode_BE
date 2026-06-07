# AuditNode Backend API Documentation

## Base URL
- **HTTPS:** `https://localhost:5001`
- **HTTP:** `http://localhost:5000`

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
Updates an existing application's metadata. The `id` is passed in the URL, and the fields to be updated are provided in the body. `AppCode` is immutable and cannot be updated.

**Request Body (`UpdateApplicationDto`):**
```json
{
  "appName": "string",
  "ownerTeam": "string",
  "risk": "string",
  "icon": "string",
  "techStack": "string"
}
```

**Response:**
- `204 No Content`: Update successful.
- `400 Bad Request`: Validation failed.
- `404 Not Found`: Application with the given ID does not exist.

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

## 7. Error Handling
Errors follow a consistent format:
```json
{
  "error": "Detailed error message"
}
```

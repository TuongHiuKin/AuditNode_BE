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
        "ownerId": "uuid",
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

---

## 2. Applications Endpoints

### GET `/api/applications`
Retrieves all registered applications.

**Response (200 OK):**
```json
[
  {
    "id": "uuid",
    "appCode": "string",
    "appName": "string",
    "ownerId": "uuid"
  }
]
```

### POST `/api/applications`
Registers a new application.

**Request Body:**
```json
{
  "appCode": "string",
  "appName": "string",
  "ownerId": "uuid"
}
```

---

## 3. Analytics Endpoints

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

## 4. Error Handling
Errors follow a consistent format:
```json
{
  "error": "Detailed error message"
}
```

# AuditNode Backend - Infrastructure Management System

**Status:** Remediation verified; database rollout pending
**Framework:** ASP.NET Core 10.0  
**Database:** PostgreSQL with Entity Framework Core  

---

## 📋 Table of Contents

- [Project Overview](#project-overview)
- [Technology Stack](#technology-stack)
- [Project Structure](#project-structure)
- [Quick Start](#quick-start)
- [Documentation](#documentation)

---

## Project Overview

AuditNode Backend is a comprehensive Infrastructure Management System built on ASP.NET Core 10.0. It manages:

- **G1 Inventory:** Server and application infrastructure tracking.
- **G3 Topology:** Application dependency mapping and visualization.
- **Universal Search:** Unified, case-insensitive search across servers and apps.
- **Multi-Datacenter Support:** Organize servers by datacenter.

## Current security and tenant baseline

- The React login/register screens authenticate through the backend gateway at `/api/v1/auth`; the application does not require the hosted Keycloak UI.
- The gateway keeps the refresh token in the secure, HttpOnly `auditnode.refresh_token` cookie scoped to `/api/v1/auth`. API access tokens are still validated as JWT bearer tokens.
- Configure Keycloak through `Keycloak:Authority`, `Keycloak:Realm`, `Keycloak:Audience`, `Keycloak:AdminClientId`, `Keycloak:AdminClientSecret`, `Keycloak:BffClientId`, and `Keycloak:BffClientSecret`. Supply values through the existing configuration/secret mechanism; never commit them.
- `GET /api/v1/workspaces` lists workspaces owned by or assigned to the authenticated user. All other tenant APIs require a non-empty `X-Workspace-Id` header and verify owner/member access before setting tenant context.
- Canonical API contracts and operational details are documented in [docs/API.md](docs/API.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and [docs/DATABASE.md](docs/DATABASE.md).

## Current verification baseline

Run from the backend directory without changing configuration values:

```powershell
dotnet test AuditNode.Tests/AuditNode.Tests.csproj --no-restore
dotnet build AuditNode.API/AuditNode.API.csproj --no-restore
dotnet ef migrations has-pending-model-changes --project AuditNode.Infrastructure/AuditNode.Infrastructure.csproj --startup-project AuditNode.API/AuditNode.API.csproj --no-build
```

The remediation baseline completed with 236/236 tests passing, the API build at zero errors, and no pending EF model changes. Known restore/build warnings remain for preview JWT bearer package resolution and the Microsoft.OpenApi advisory; review dependency updates separately rather than changing packages during a database rollout.

> Database warning: the three remediation migrations and the workspace-scoped view SQL described in [docs/DATABASE.md](docs/DATABASE.md) have been generated/reviewed but **have not been applied to a live database**.

### Key Features

✅ RESTful API with async/await patterns  
✅ Universal Search Engine (Servers & Applications)  
✅ Entity Framework Core with PostgreSQL  
✅ Keyless read-only database views for optimized queries  
✅ CORS enabled for React frontend  
✅ Clean architecture (Models → Data → Controllers)  

---

## Technology Stack

| Component | Version | Purpose |
|-----------|---------|---------|
| .NET SDK | 10.0 | Runtime framework |
| C# | 13 | Language |
| Entity Framework Core | 10.0.x | ORM |
| Npgsql | - | PostgreSQL provider |
| PostgreSQL | - | Data persistence |

---

## Project Structure

The project follows a **Clean Architecture** pattern, divided into four main projects:

- **`AuditNode.API`**: The entry point. Contains ASP.NET Core Controllers, Middleware, and API configuration.
- **`AuditNode.Application`**: Business logic layer. Contains DTOs (Data Transfer Objects), Repository Interfaces, and Service logic.
- **`AuditNode.Domain`**: Core layer. Contains Database Entities and Domain Models. Has zero dependencies on other projects.
- **`AuditNode.Infrastructure`**: Implementation layer. Contains `AuditDbContext`, Repository implementations, and external service integrations.

```
AuditNode.Backend/
├── AuditNode.API/           # Controllers & Startup
├── AuditNode.Application/   # DTOs & Interfaces
├── AuditNode.Domain/        # Entities & Domain Models
├── AuditNode.Infrastructure/# Data Access (EF Core)
├── docs/                    # Detailed Documentation
└── AuditNode.slnx           # Visual Studio Solution File
```

---

## Quick Start

### Prerequisites
- .NET 10.0 SDK
- PostgreSQL (running locally)

### Steps
1. **Clone & Setup**
2. **Provide Runtime Configuration** through the approved local secret/environment mechanism. Preserve the tracked connection string, ports, and existing configuration notes.
3. **Review Database Rollout** in [docs/DATABASE.md](docs/DATABASE.md). Do not apply pending migrations or view SQL to a live database without backup/backfill checks and approval.
4. **Run Application**:
   ```bash
   dotnet build
   dotnet run
   ```

---

## Documentation

For detailed information, please refer to the following:

- 📖 [API Documentation](docs/API.md) - Endpoints, DTOs, and examples.
- 🗄️ [Database Guide](docs/DATABASE.md) - Schema, Relationships, and Views.
- 🏗️ [Architecture Overview](docs/ARCHITECTURE.md) - Design patterns and UI visualization logic.
- 🕒 [Maintenance History](docs/HISTORY.md) - Records of refactorings and fixes.

---

## 🚀 Current Progress (August 2026)

- **Test Suite**: The remediation baseline passes **236/236 tests**; this is a passing-suite count, not a claim of complete code coverage.
- **Identity Management**: Custom backend login/register/refresh/logout gateway with Keycloak JWT bearer validation and a secure refresh cookie.
- **Workspace Authorization**: Owner/member validation, required workspace header semantics, and tenant-filtered persistence.
- **Backend Architecture**: Implemented **Clean Architecture** patterns for Server and Application management, decoupling API from data access.
- **Universal Search**: Implemented unified results across servers and apps with hosting context.
- **Data Integrity**: Tenant-composite uniqueness, explicit deployment identity, application labels, canonical topology state, and idempotent dependency synchronization.

## 🛠️ Next Steps

- **Audit Logging**: Implement a comprehensive audit trail for infrastructure changes.
- **Real-time Monitoring**: Integrate Prometheus/Grafana hooks for live server status updates.
- **Reporting**: Exportable PDF/Excel reports for G1/G3 inventory compliance.

---

**Last Updated:** August 18, 2026
**Maintainer:** Lead Architect & DevOps Team

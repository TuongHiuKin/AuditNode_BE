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

## Current security and catalog baseline

- The React login/register screens authenticate through the backend gateway at `/api/v1/auth`. Identity is owned by the API itself (ASP.NET Core Identity); no external identity provider is required.
- The gateway keeps the refresh token in the secure, HttpOnly `auditnode.refresh_token` cookie scoped to `/api/v1/auth`. Access tokens are self-signed JWTs (HMAC-SHA256) validated by the same API.
- Configure identity through `Identity:Issuer`, `Identity:Audience`, and `Identity:SigningKey`. The signing key must be at least 32 bytes and is supplied through environment variables or an untracked settings file; never commit it. Startup fails fast when it is missing or too short.
- Refresh tokens are opaque values stored as SHA-256 hashes and rotated on every use, so a leaked database read cannot replay a session. Disabling a user revokes their outstanding refresh tokens.
- The first administrator is created from `Identity:BootstrapAdmin:Username|Email|Password` when those keys are configured. Leave them unset to manage seeding yourself.
- Runtime inventory is a Global Catalog partitioned by authenticated `owner_user_id`; there is no Workspace header or Workspace runtime middleware.
- Owners can grant Viewer or Editor access to labeled resources through Label Grants. Anonymous share tokens are separate, revocable Viewer capabilities.
- Canonical API contracts and operational details are documented in [docs/API.md](docs/API.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and [docs/DATABASE.md](docs/DATABASE.md).

## Current verification baseline

Run the fast verification path from the backend directory without changing configuration values:

```powershell
dotnet restore AuditNode.slnx --locked-mode
dotnet build AuditNode.slnx --configuration Release --no-restore
dotnet test AuditNode.Tests/AuditNode.Tests.csproj --configuration Release --no-build --filter "Category!=PostgreSQL"
dotnet ef migrations has-pending-model-changes --project AuditNode.Infrastructure/AuditNode.Infrastructure.csproj --startup-project AuditNode.API/AuditNode.API.csproj --configuration Release --no-build
```

The 17 PostgreSQL transaction, concurrency, constraint, and migration tests are marked `Category=PostgreSQL`. They require an explicitly configured disposable database via `AUDITNODE_TEST_POSTGRES`; CI migrates PostgreSQL 17, runs only that category, writes TRX, and calls `scripts/Verify-Trx.ps1` so a zero-test, skipped, not-executed, or failed relational run cannot pass.

> Database warning: this repository does not assert that any migration has been applied to a production database. The Workspace-to-Global-Catalog cutover is destructive by design and requires the approved backup/reset procedure described in [docs/DATABASE.md](docs/DATABASE.md).

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

## 🚀 Current Progress (September 2026)

- **Test Suite**: Fast and PostgreSQL suites are separately selected and reported; required PostgreSQL execution is checked from TRX rather than inferred from the test command's exit code.
- **Identity Management**: Custom backend login/register/refresh/logout gateway backed by ASP.NET Core Identity, with self-signed JWTs and a secure refresh cookie.
- **Catalog Authorization**: Owner-partitioned resources, direct Label Grants, and revocable anonymous Viewer share tokens; no Workspace runtime header is used.
- **Backend Architecture**: Implemented **Clean Architecture** patterns for Server and Application management, decoupling API from data access.
- **Universal Search**: Implemented unified results across servers and apps with hosting context.
- **Data Integrity**: Tenant-composite uniqueness, explicit deployment identity, application labels, canonical topology state, and idempotent dependency synchronization.

## 🛠️ Next Steps

- **Audit Logging**: Implement a comprehensive audit trail for infrastructure changes.
- **Real-time Monitoring**: Integrate Prometheus/Grafana hooks for live server status updates.
- **Reporting**: Exportable PDF/Excel reports for G1/G3 inventory compliance.

---

**Last Updated:** September 6, 2026
**Maintainer:** Lead Architect & DevOps Team

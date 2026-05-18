# AuditNode Backend - Infrastructure Management System

**Status:** ✅ Production Ready  
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
- **Multi-Datacenter Support:** Organize servers by datacenter.
- **Keycloak Integration:** UUID-based owner identification.

### Key Features

✅ RESTful API with async/await patterns  
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
2. **Update Connection String** in `appsettings.json`.
3. **Initialize Database**: Run the SQL schema provided in documentation.
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
- 🕒 [Maintenance History](docs/HISTORY.md) - Records of refactorings and fixes.

---

**Last Updated:** May 17, 2026  
**Maintainer:** DevOps Team

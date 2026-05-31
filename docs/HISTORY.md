# Maintenance & Refactoring History

This document tracks significant changes, refactorings, and bug fixes applied to the AuditNode Backend.

---

## 📅 May 31, 2026 - Datacenter API Refactor for Frontend Dropdown
**Status:** ✅ Complete

### Changes:
- **Lightweight DTO**: Created `DatacenterDto` containing only `Id` and `Name` to minimize payload for frontend dropdowns.
- **Controller Refactor**: Updated `DatacentersController.GetDatacenters` to return `IEnumerable<DatacenterDto>` instead of the full `Datacenter` entity.
- **TDD Implementation**: Created `DatacentersControllerTests.cs` using xUnit, Moq, and FluentAssertions to verify the new DTO mapping and controller behavior.
- **HTTP Testing**: Updated `AuditNode.API.http` with a new request for the datacenters endpoint.

### Impact:
- **Performance**: Reduced network payload size for the datacenters listing.
- **Consistency**: Follows the project's Clean Architecture standards for DTO usage.
- **Maintainability**: New unit tests ensure the endpoint remains stable during future refactorings.

---

## 📅 May 28, 2026 - Application Registration "Find or Create" (Upsert) Implementation
**Status:** ✅ Complete

### Changes:
- **Upsert Logic Implementation**: Refactored `ApplicationRepository.RegisterApplicationAsync` to check for existing applications by `AppCode`. If found, the application is updated and a new `PortMapping` is added; otherwise, a new application is created.
- **Transaction Support**: Wrapped the registration logic in a database transaction to ensure atomicity.
- **OwnerTeam Refactor**: Renamed `OwnerId` to `OwnerTeam` across Domain, Application, and API layers to reflect team-based ownership.
- **Unique Constraint**: Added an explicit unique index on `AppCode` in `AuditDbContext` and updated the PostgreSQL schema (conceptually, via EF Core model).
- **Enum Cleanup**: Deleted `RiskLevel.cs` and moved to string-based risk management ("LOW", "MEDIUM", "HIGH") for better flexibility and consistency with frontend requirements.
- **TDD Compliance**: Added unit tests to `ApplicationRepositoryTests` verifying "Find or Create" behavior.

### Impact:
- **Scalability**: Allows a single application definition to be deployed across multiple servers via port mappings without violating unique constraints.
- **Data Integrity**: Prevents duplicate `AppCode` entries while allowing rich relationship mapping.

---

## 📅 May 24, 2026 - Application OwnerId Schema Alignment
**Status:** ✅ Complete

### Changes:
- **`OwnerId` Type Refactor**: Reverted `OwnerId` from `Guid` to `string` (VARCHAR(255)) in `Application.cs` and related DTOs to align with the updated database schema.
- **Fluent API Update**: Configured `OwnerId` in `AuditDbContext` to explicitly use `character varying(255)` column type.
- **Validation Update**: Updated `ApplicationsController` to use `string.IsNullOrWhiteSpace` for `OwnerId` validation.
- **DTO Consistency**: Synchronized `CreateApplicationDto`, `ApplicationResponseDto`, and `ServerResponseDto` to use `string` for `OwnerId`.

### Impact:
- **Flexibility**: Supports non-UUID owner identifiers while maintaining compatibility with Keycloak UUID strings.
- **Schema Alignment**: Matches the target database's `VARCHAR` definition for the `owner_id` column.

---

## 📅 May 18, 2026 - Project Documentation Synchronization
**Status:** ✅ Complete

### Changes:
- **`README.md` Update**: Corrected project structure to reflect the 4-project Clean Architecture setup (`API`, `Application`, `Domain`, `Infrastructure`).
- **`API.md` Refinement**: Updated DTO schemas for `ServerResponseDto`, `Analytics/Topology`, and `Analytics/Dependencies` to match the current C# implementations.
- **`DATABASE.md` Detail**: Expanded documentation for `v_topology_map` and `v_dependency_graph` with full column listings and EF Core mapping details.
- **Project Structure Alignment**: Ensured all documentation accurately points to the correct namespaces and folders.

### Impact:
- Documentation is now fully synchronized with the codebase, providing a reliable reference for developers and for context-loading in future AI sessions.

---

## 📅 May 16, 2026 - Application Model Data Type Fix
**Status:** ✅ Complete

### Changes:
- **`OwnerId` Type Fix**: Changed from `string` to `Guid` in `Application.cs` and related DTOs to match PostgreSQL `uuid` type.
- **`Description` Removal**: Removed `Description` property from `Application.cs` and `ApplicationsController` as it was not present in the database schema.
- **Validation Update**: Updated `PostApplication` to validate `OwnerId != Guid.Empty` instead of string null checks.

### Impact:
- **Breaking Change**: API clients must now send `ownerId` as a valid UUID string and should not include `description`.

---

## 📅 May 16, 2026 - Server & Application Model Refactoring
**Status:** ✅ Complete

### Changes:
- **Server Model Expansion**: Added `DatacenterId` (Guid) and `Status` (string) to `Server.cs`.
- **Application Model Cleanup**:
  - Renamed `Owner` to `OwnerId` (initially string, later fixed to Guid).
  - Removed `RiskLevel` property to align with schema.
  - Added `[Column]` attributes to all properties for explicit snake_case mapping.
- **Controller Alignment**: Updated `ServersController` and `ApplicationsController` DTOs and logic to support new/modified fields.

---

## Summary of Refactoring Benefits:
1. **Schema Compliance**: All C# models now map 1:1 with PostgreSQL table columns.
2. **Type Safety**: Use of `Guid` for UUID columns prevents runtime mapping errors.
3. **Keycloak Readiness**: `OwnerId` is ready to store Keycloak user UUIDs.
4. **Clean Code**: Removed dead properties (`RiskLevel`, `Description`) that caused database mapping failures.

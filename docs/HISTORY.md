# Maintenance & Refactoring History

This document tracks significant changes, refactorings, and bug fixes applied to the AuditNode Backend.

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

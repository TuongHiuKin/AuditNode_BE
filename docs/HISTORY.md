# Maintenance & Refactoring History

This document tracks significant changes, refactorings, and bug fixes applied to the AuditNode Backend.

---

## 📅 June 3, 2026 - Feature: Universal Search API Refactoring & Optimization
**Status:** ✅ Complete

### Changes:
- **Server-Side Evaluation**: Refactored LINQ queries to use direct projection (`.Select()`) into `SearchResultDto`, ensuring all filtering and transformation happen at the SQL level (PostgreSQL).
- **Duplication Prevention**: Optimized the Application-Server join using subqueries within the projection to fetch hosting context (Server/Port) without multiplying application records.
- **Criteria Cleanup**: Removed all risk-related metadata from the search logic to focus strictly on identifiers (Hostname, IP, AppCode, AppName).
- **Result Throttling**: Implemented strict `Top 20` limiting within the IQueryable pipeline to prevent over-fetching and massive payloads.
- **Portability Fix**: Standardized on `.ToLower().Contains()` to ensure compatibility across both the `InMemory` test provider and the production `Npgsql` provider, while maintaining index friendliness.
- **TDD Verification**: Confirmed all 6 test cases pass with the optimized logic.

### Impact:
- **Performance**: Significant reduction in memory overhead and database execution time.
- **Reliability**: Eliminated duplicate records in search results caused by many-to-many port mappings.

---

## 📅 June 2, 2026 - Bug Fix: Port Mapping Unique Constraint Violation in Bulk Import
**Status:** ✅ Complete

### Changes:
- **Implemented Port Collision Detection**: Resolved `duplicate key value violates unique constraint "port_mappings_server_id_port_number_key"` by adding pre-persistence checks.
- **Pre-fetch Strategy**: All existing `PortMapping` entries for involved servers are now batch-fetched into an in-memory dictionary (`mappingLookup`) using a `ServerId:PortNumber` key.
- **Idempotency Support**: If an identical mapping (same Server, Port, App, and Protocol) already exists, the logic skips insertion while still counting the row as successful.
- **Conflict Reporting**: If a port is already mapped to a *different* application or protocol, a detailed conflict message is returned, and the row is skipped.
- **In-Memory Tracking**: Updated the tracking dictionary during the import loop to prevent duplicate mapping insertions within the same Excel file.

### Impact:
- **Transactional Integrity**: Prevents whole-batch failures caused by single-port collisions.
- **Data Accuracy**: Ensures that port assignments are correctly audited and attributed to the right applications.

---

## 📅 June 2, 2026 - Performance Optimization: In-Memory Dictionary Pattern in Bulk Import
**Status:** ✅ Complete

### Changes:
- **Eliminated N+1 Queries**: Replaced loop-based database lookups for Applications and Servers with a pre-fetched dictionary pattern.
- **Batch Pre-fetching**: All `AppCode`s and `IpAddress`es are now extracted from the Excel file upfront and queried in single batches using `.Where(x => list.Contains(x.Key)).ToDictionaryAsync()`.
- **Deterministic Queries**: Resolved EF Core warning 10103 by adding `.OrderBy(x => x.Id)` before `.FirstOrDefaultAsync()` calls, ensuring strictly consistent results.
- **Improved Validation**: The conflict detection logic now utilizes the pre-fetched `existingAppsDict` for O(1) in-memory lookups instead of O(N) database calls.

### Impact:
- **Scalability**: Dramatically reduced database load and execution time for large Excel files.
- **Stability**: Resolved non-deterministic query warnings that could lead to inconsistent data states in distributed environments.

---

## 📅 June 2, 2026 - Bug Fix: Duplicate Server Key Violation in Bulk Import
**Status:** ✅ Complete

### Changes:
- **Refactored Upsert Logic**: Resolved `duplicate key value violates unique constraint "servers_ip_address_key"` by implementing proper entity grouping.
- **Entity Grouping**: Implemented a multi-step process for bulk import:
    1. Group incoming rows by `IpAddress` to identify distinct servers.
    2. Batch-fetch existing servers and insert only new ones in a single `SaveChangesAsync` call.
    3. Group rows by `AppCode` to identify distinct applications.
    4. Batch-fetch and upsert applications.
    5. Finalize by adding all `PortMapping` entries.
- **TDD Verification**: Added a new test case `ImportInventoryAsync_ShouldHandleMultipleRowsForSameNewServer` to `InventoryImportServiceTests.cs` to ensure the scenario is handled correctly.
- **Test Infrastructure**: Configured the in-memory database to ignore transaction warnings, allowing for more realistic service testing.

### Impact:
- **System Stability**: Prevents crashes during bulk imports when multiple applications reside on the same newly registered server.
- **Performance**: Optimized database interactions by batching fetches and insertions.

---

## 📅 June 2, 2026 - Best-Effort Bulk Import & Excel Template Generation
**Status:** ✅ Complete

### Changes:
- **Bulk Import Engine**: Implemented `InventoryImportService` using **ClosedXML** to process `.xlsx` files with a "Best-Effort" approach.
- **Transactional Upsert**: Developed atomic transactional logic to upsert Servers (by IP) and Applications (by AppCode), ensuring data consistency across multiple entities (Servers, Apps, PortMappings).
- **Conflict Resolution**: Implemented logic to detect and report metadata conflicts (e.g., AppCode reuse with different AppNames) while skipping only the conflicting rows.
- **Template Generation**: Created a dynamic Excel template generator (`GET /api/inventory/import-template`) with built-in data validation dropdowns for Environment and Protocol.
- **Clean Architecture Integration**: Registered `IInventoryImportService` in the DI container and implemented `InventoryImportController` in the API layer.
- **Unit Testing**: Added `InventoryImportServiceTests.cs` using an in-memory database to verify validation, conflict logic, and template structure.

### Impact:
- **Efficiency**: Users can now import hundreds of infrastructure nodes in seconds via a single Excel upload.
- **Data Quality**: Excel data validation and backend conflict detection prevent corrupt or inconsistent data from entering the system.
- **User Experience**: Detailed Best-Effort responses provide clear feedback on which rows were saved, errored, or conflicted.

---

## 📅 June 1, 2026 - UI Topology Refactor & Backend Transaction Upsert
**Status:** ✅ Complete

### Changes:
- **Refactored UI Topology**: Implemented "Static Resource Inventory" approach using React Flow with nested nodes (Servers as containers for Applications).
- **Backend Transaction Upsert**: Implemented atomic transaction-based registration in `ApplicationRepository` to support many-to-many mapping and prevent duplicate `AppCode` entries.
- **UI Bug Fixes**: Resolved z-index layering issues in React Flow and disabled node dragging to maintain a stable static layout.
- **State Synchronization**: Implemented on-navigate state synchronization to maintain UI consistency when switching between infrastructure views.
- **Documentation Overhaul**: Synchronized `README.md`, `API.md`, `DATABASE.md`, and created `ARCHITECTURE.md` to reflect the current system state.

### Impact:
- **Consistency**: Users experience a stable, reliable visualization of infrastructure.
- **Scalability**: The backend can now handle complex application-to-server mappings without data corruption.
- **Project Maturity**: Documentation now serves as a comprehensive "checkpoint" for the project's current state.

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

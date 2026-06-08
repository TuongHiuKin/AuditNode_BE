# Prompt Archive: Universal Search API Implementation

**Date:** June 3, 2026
**Status:** Success ✅

## 1. Requirement Summary
Implement a Universal Search API for an auditing and resource inventory system.
- **Feature**: `GET /api/search?keyword=...` – Unified search across Servers and Applications.
- **DTO**: `SearchResultDto` with `Id`, `Type` (SERVER/APP), `Title`, `Subtitle`, and `MatchReason`.
- **Logic**: 
  - Case-insensitive search on Server Hostname/IP and Application Name/Code.
  - Subtitle includes hosting context: "On Server: Hostname (Port: X)".
  - Result limit of 20.
  - Keyword length validation (min 2 chars).
- **Architecture**: Clean Architecture, EF Core direct queries (no caching), Strict TDD.

## 2. Core Implementation Strategy
### Backend Logic:
- **Service Layer**: Created `IInventorySearchService` and `InventorySearchService`.
- **Unified Query**:
  - Performed two separate EF Core queries for Servers and Applications.
  - Server search matches `Hostname` or `IpAddress`.
  - Application search matches `AppName` or `AppCode`, including `Server` and `PortMappings` for subtitle context.
- **Consolidation**: Concatenated results, took top 20, and returned as a list of `SearchResultDto`.
- **Case-Insensitivity**: Used `.ToLower().Contains()` for compatibility with both InMemory and Npgsql providers.

## 3. Key Code Structures
```csharp
// Search Logic Implementation
var serverResults = await _context.Servers
    .Where(s => s.Hostname.ToLower().Contains(lowerKeyword) || s.IpAddress.ToLower().Contains(lowerKeyword))
    .Select(s => new SearchResultDto { ... })
    .ToListAsync();

var appResults = await _context.Applications
    .Include(a => a.Server)
    .Include(a => a.PortMappings)
    .Where(a => a.AppName.ToLower().Contains(lowerKeyword) || a.AppCode.ToLower().Contains(lowerKeyword))
    .Select(a => new SearchResultDto { ... })
    .ToListAsync();

return serverResults.Concat(appResults).Take(20).ToList();
```

## 4. Verification (TDD)
- **InventorySearchServiceTests.cs**:
  - `SearchAsync_ShouldReturnEmpty_WhenKeywordIsShortOrNull`: Verified validation logic.
  - `SearchAsync_ShouldReturnServer_WhenHostnameMatches`: Verified server matching and reason.
  - `SearchAsync_ShouldReturnApp_WhenAppNameMatches`: Verified application matching with hosting context in subtitle.
  - `SearchAsync_ShouldLimitResultsTo20`: Verified the pagination/limit logic.

## 5. Documentation
- Updated `API.md` with new search endpoint documentation.
- Updated `HISTORY.md` with implementation milestone.
- Updated `README.md` to highlight the new search capability.

---

# Prompt Archive: Best-Effort Bulk Import & Excel Template Generation

**Date:** June 2, 2026
**Status:** Success ✅

## 1. Requirement Summary
Implement a Best-Effort Bulk Import system for topology inventory using `ClosedXML`.
- **Feature 1**: `GET /api/inventory/import-template` – Generates an .xlsx template with data validation dropdowns for Environment (Production, Development) and Protocol (TCP, UDP, etc.).
- **Feature 2**: `POST /api/inventory/import` – Processes uploaded .xlsx files. Upserts Servers (IP) and Applications (AppCode). Reports totals, errors (validation), and conflicts (metadata mismatch).
- **Architecture**: Clean Architecture (.NET 10), transactional integrity.

## 2. Core Implementation Strategy
### Backend Logic:
- **Template Generation**: Used `worksheet.Range().SetDataValidation().List()` for dropdowns and `XLColor` for styling.
- **Bulk Import (Optimized for Duplicates & Performance)**:
  - **Eliminated N+1 Queries**: Extracted all `AppCode`s and `IpAddress`es from the Excel file upfront and pre-fetched them into Dictionaries in single batches.
  - **In-Memory Dictionary Pattern**: Replaced loop-based database lookups with O(1) memory lookups for Applications and Servers during validation and upsert.
  - **Deterministic Results**: Added `.OrderBy(x => x.Id)` before `.FirstOrDefaultAsync()` to fix EF Core warning 10103 and ensure consistent behavior.
  - **Entity Grouping**: Grouped valid rows by `IpAddress` (Servers) and `AppCode` (Applications) to identify distinct entities.
  - **Batch Upsert**:
    1. Fetched existing entities in single batches.
    2. Inserted only missing entities and persisted them to resolve IDs.
    3. **Port Mapping Idempotency**: Pre-fetched existing `PortMapping` entries for involved servers.
    4. **Collision Detection**: Checked for `(ServerId, PortNumber)` uniqueness. Skipped identical mappings (idempotency) and reported collisions (different app/protocol) as conflicts.
    5. Mapped all valid `PortMapping` entries using the resolved entity IDs.
  - **Transaction**: Wrapped the entire process in `BeginTransactionAsync`.
- **DTOs**: `ImportResponseDto`, `ImportErrorDto`, `ImportConflictDto`.

## 3. Key Code Structures (Infrastructure Layer)
```csharp
// Template Validation Example
worksheet.Range("C2:C1000").SetDataValidation().List("Production,Development", true);

// Best-Effort Processing Loop
foreach (var row in rows) {
    // 1. Pre-validate fields
    // 2. Conflict metadata check (AppCode exists with different data?)
    // 3. Add to validRows list
}

// Transactional Upsert
using var transaction = await _context.Database.BeginTransactionAsync();
// ... Find/Create Server -> Find/Create App -> Create PortMapping ...
await transaction.CommitAsync();
```

## 4. Verification (TDD)
- **InventoryImportServiceTests.cs**:
  - `GenerateTemplate_ShouldReturnValidExcelFile`: Verified headers and not-null result.
  - `ImportInventoryAsync_ShouldProcessValidRows`: Verified database counts after valid import.
  - `ImportInventoryAsync_ShouldReportConflicts`: Verified conflict detection for existing AppCodes.

## 5. Documentation
- Updated `API.md` with endpoints and sample response.
- Updated `HISTORY.md` with implementation summary.
- Updated `README.md` (via standard documentation sync).

---

# Prompt Archive: Infrastructure Endpoints (Migration & Safe Purge)

**Date:** June 8, 2026
**Status:** Success ✅

## 1. Requirement Summary
Implement infrastructure management endpoints for application lifecycle.
- **Feature 1**: `GET /api/infrastructure/apps/{id}/dependencies-count` – Pre-check for inbound/outbound dependencies.
- **Feature 2**: `PUT /api/infrastructure/apps/migrate` – Update application server/port binding.
- **Feature 3**: `DELETE /api/infrastructure/apps/{id}/purge` – Transactional cascading hard delete.
- **Architecture**: Clean Architecture, explicit `IDbContextTransaction`, logic bug fix for inbound connection counting.

## 2. Core Implementation Strategy
### Backend Logic:
- **Dependency Counting (Fixed)**: 
    - Fetches all `PortMappingIds` for the application.
    - Counts `AppDependencies` where the app is `SourceAppId` OR `DestAppId` OR `DestPortId` is in the mapping list. This ensures full coverage of inbound/outbound connections.
- **Migration Logic**:
    - Updates `ServerId` and `PortNumber` in the `PortMapping` entity.
    - Wrapped in an `IDbContextTransaction` to ensure atomicity.
- **Safe Purge (Cascading)**:
    - Executed in a strict sequential order to satisfy PostgreSQL FK constraints:
        1. Delete `AppDependencies` (both inbound and outbound).
        2. Delete `PortMappings`.
        3. Delete root `Application`.
    - Entire operation is transactional with logging and rollback guards.
- **Service Layer**: Implemented in `AuditNode.Infrastructure` to access `AuditDbContext` and handle transactions, with the interface in `AuditNode.Application`.

## 3. Key Code Structures
```csharp
// Purge Logic - Sequential Deletion
using var transaction = await _context.Database.BeginTransactionAsync();
// 1. Dependencies
_context.AppDependencies.RemoveRange(dependenciesToDelete);
// 2. Port Mappings
_context.PortMappings.RemoveRange(portMappingsToDelete);
// 3. Application
_context.Applications.Remove(appToDelete);
await transaction.CommitAsync();
```

## 4. Verification (TDD)
- **InfrastructureServiceTests.cs**:
  - `GetDependenciesCountAsync_ShouldCountBothInboundAndOutbound`: Verified the fixed counting logic.
  - `MigrateAppAsync_ShouldUpdatePortMapping`: Verified transactional update.
  - `PurgeAppAsync_ShouldDeleteApplicationAndDependencies`: Verified full cascading deletion and referential integrity.

## 5. Documentation
- Updated `API.md` with new infrastructure endpoints.
- Updated `HISTORY.md` with implementation milestone.
- Updated `DATABASE.md` with cascading purge logic details.

**Date:** June 7, 2026
**Status:** Success ✅

## 1. Requirement Summary
Align C# code with database schema change (dropped `server_id` from `applications`) to enforce proper many-to-many data normalization via the `port_mappings` junction table.

## 2. Core Implementation Strategy
### Backend Logic:
- **Entity Cleanup**: Removed `ServerId` and `Server` property from `Application.cs`. Removed `Applications` from `Server.cs`.
- **DbContext Configuration**: Updated `AuditDbContext.cs` to remove the direct ForeignKey mapping for `Application.Server`.
- **DTO & Input Refactoring**: Removed `ServerId`, `PortNumber`, and `Protocol` from `CreateApplicationDto.cs` to allow independent application registration.
- **Query Refactoring (LINQ)**:
    - Updated `ApplicationService.CreateAsync` to register apps without immediate server binding.
    - Updated `InventorySearchService.cs` to use `.Include(a => a.PortMappings).ThenInclude(pm => pm.Server)` for retrieving hosting details.
    - Updated `InventoryImportService.cs` to decouple app upserts from server assignments.
- **Service Layer Implementation**: Standardized the use of nested double-includes for relational loading.

## 3. Key Code Structures
```csharp
// Refactored Search Logic Projection
Subtitle = $"On Server: {(a.PortMappings.OrderBy(pm => pm.PortNumber).Select(pm => pm.Server.Hostname).FirstOrDefault() ?? "Unknown")} (Port: {(a.PortMappings.OrderBy(p => p.PortNumber).Select(p => p.PortNumber.ToString()).FirstOrDefault() ?? "N/A")})"
```

## 4. Verification (TDD)
- **Updated Test Suites**:
    - `ApplicationRepositoryTests.cs`: Verified app registration and retrieval without `ServerId`.
    - `InventoryImportServiceTests.cs`: Verified bulk import still correctly creates mappings.
    - `InventorySearchServiceTests.cs`: Verified search results correctly display hosting context via junction table.
- **Test Results**: All 46 tests passed successfully.

## 5. Documentation
- Updated `API.md` for independent application registration.
- Updated `HISTORY.md` with normalization milestone.
- Updated `PROMPT_HISTORY.md` (Self-referential log).


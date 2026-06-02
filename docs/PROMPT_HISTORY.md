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

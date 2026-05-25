# PROMPT_HISTORY.md

## [2026-05-21] Backend Topology & Dependency Manager Implementation

**Prompt:**
Implement the Backend API and Database Models to support the finalized Topology Tree and Dependency Manager UI.

1. DATABASE MODELS & RELATIONSHIPS (Domain & Infrastructure):
- Ensure Entity Framework Core models support a hierarchical infrastructure relationship: Datacenter (1-to-1) -> Servers (1-to-N) -> Applications/Ports.
- An Application model must include properties: Id, Name, PortNumber, Protocol (e.g., 443, 8080, 5432), RiskLevel (e.g., HIGH, MEDIUM, LOW), and an optional TargetApplicationId to represent connections (like User Auth pointing to Main DB).

2. OPTIMIZED API ENDPOINTS (API & Application Layers):
- Implement 'GET /api/topology/tree': Fetch a complete hierarchical tree structured DTO matching the Datacenter -> Server -> Port structure. Use EF Core '.Include()' and '.ThenInclude()' explicitly to prevent N+1 query performance lag. Accept query parameters: 'int? datacenterId', 'int take', 'int skip' for pagination control.
- Implement 'GET /api/dependency/map': Return a flat list of Servers, their child Apps, and explicit connection arrays (sourceAppId, targetAppId) so the frontend can draw the Bezier lines dynamically.
- Implement 'POST /api/servers' and 'POST /api/datacenters' with FluentValidation to enforce unique IP/Port constraints.

3. TDD UNIT TESTS:
- Inside the xUnit test project, write integration/unit tests using FluentAssertions to verify that fetching the topology tree respects the 'take/skip' boundaries and ensures valid JSON contracts are returned.

**Outcome:**
- Successfully created `Datacenter` entity and updated `Server`/`Application` for hierarchical structure.
- Implemented `TopologyController` with optimized Tree and Map endpoints.
- Integrated FluentValidation for robust request validation.
- Added comprehensive unit tests in `AuditNode.Tests` (19 passing tests).
- All changes adhere to .NET Clean Architecture and TDD standards.

## [2026-05-21] Backend API Performance Optimization & Stability

**Prompt:**
Optimize the Backend API performance layer to serve real-time infrastructure data to the UI.

1. READ-ONLY PERFORMANCE OPTIMIZATION:
- Refactor the EF Core query logic inside the 'GET /api/topology/tree' and 'GET /api/dependency/map' endpoints to explicitly use '.AsNoTracking()'. This is strict to bypass change-tracking overhead and minimize server memory usage under large-scale node infrastructure.
- In the controller, implement a protective default guard: if the 'take' query parameter is omitted by the client, default it to a maximum boundary threshold of 100 top-level Server nodes to protect network bandwidth from massive JSON payload dumps.

2. STABLE REST API CONTRACTS:
- Double-check that 'GET /api/dependency/map' cleanly outputs an un-nested, clean JSON schema containing a flat list of Server models, their assigned child Application pills (including PortNumber and dynamic RiskLevel), and an explicit connection vector array (holding sourceAppId and targetAppId) to map network edges.

3. TDD UNIT TESTS:
- Update your xUnit tests to verify that the query handlers correctly implement the default 'take' threshold and return clean DTOs with successful 200 OK statuses when database collections are completely empty.

**Outcome:**
- Refactored `TopologyRepository` to use `.AsNoTracking()` in all read operations.
- Optimized domain models by adding `Server.Applications` collection for more direct hierarchical querying.
- Implemented a default `take` guard of 100 in `TopologyController` with a hard limit enforcement.
- Verified `DependencyMapDto` structure for clean, un-nested JSON output.
- Updated xUnit tests to cover default thresholds, large `take` capping, and empty database scenarios (21 passing tests).

## [2026-05-23] Native OpenAPI 3.1 & Scalar API Reference Implementation

**Prompt:**
Act as a Senior .NET Architect. Implement native OpenAPI 3.1 support and Scalar API Reference UI.

1. PACKAGE INSTALLATION:
- Install 'Microsoft.AspNetCore.OpenApi'.
- Install 'Scalar.AspNetCore'.

2. PROGRAM.CS CONFIGURATION:
- Register OpenAPI services with metadata: Title "AuditNode API", Version "v1", Description "Infrastructure Audit, Port & Monitoring API Gateway".
- Map OpenAPI endpoint and Scalar UI reference (/scalar/v1) in development environment.
- Correctly sequence 'UseHttpsRedirection()' and 'UseAuthorization()'.

3. API DISCOVERY COMPLIANCE:
- Verify controllers use '[ApiController]' and proper attribute routing.
- Ensure JSON contract schema streams at '/openapi/v1.json' in camelCase.

**Outcome:**
- Installed Scalar.AspNetCore and configured professional metadata via DocumentTransformer.
- Enabled Scalar UI with Moon theme at /scalar/v1.
- Verified discovery compliance across all infrastructure controllers.
- Validated integrity via successful build and 21 passing xUnit tests.

## [2026-05-24] Application OwnerId Data Type Refactor (Guid to String)

**Prompt:**
Refactor the 'OwnerId' data type mapping inside the Domain Entity configurations to align with the updated VARCHAR database schema.

1. ENTITY MODEL REFACTOR (Domain/Entities/Application.cs):
- Locate the 'Application' core domain entity.
- Change 'OwnerId' property data type from 'Guid' (or 'Guid?') to 'string':
  ```csharp
  public string OwnerId { get; set; } = string.Empty;
  ```

2. EF CORE FLUENT API CONFIGURATION ALIGNMENT (Infrastructure/Data/AuditDbContext.cs):
- Update the property builder block to explicitly inform EF Core that 'OwnerId' is a string/varchar field:
  ```csharp
  builder.Entity<Application>()
         .Property(a => a.OwnerId)
         .HasColumnName("owner_id")
         .HasColumnType("character varying")
         .HasMaxLength(255);
  ```

3. DTO & CONTROLLER PROPAGATION:
- Synchronize `CreateApplicationDto`, `ApplicationResponseDto`, and `ServerResponseDto` to use `string` for `OwnerId`.
- Update `ApplicationsController` to use `string.IsNullOrWhiteSpace` for `OwnerId` validation instead of `Guid.Empty`.

4. DOCUMENTATION SYNC:
- Update `API.md`, `HISTORY.md`, and `tech-stack-summary-be.md` to reflect the change from UUID/Guid to string/VARCHAR.

**Outcome:**
- Successfully refactored `OwnerId` from `Guid` to `string` across all architecture layers (Domain, Infrastructure, Application, API).
- Configured explicit PostgreSQL `character varying(255)` mapping in EF Core.
- Synchronized all related DTOs and validation logic.
- Updated project documentation to maintain a single source of truth.
- Verified code consistency through manual audit and structural validation.

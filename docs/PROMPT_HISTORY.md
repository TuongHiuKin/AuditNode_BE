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

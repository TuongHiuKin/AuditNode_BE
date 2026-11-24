# AuditNode System Architecture

## Current backend architecture (September 2026)

The backend preserves Clean Architecture boundaries: domain entities and invariants do not depend on infrastructure; application DTOs/interfaces define use cases; infrastructure implements EF Core repositories and external identity services; API controllers and middleware translate HTTP/authentication concerns.

### Authentication and catalog boundary

The custom `/api/v1/auth` gateway lets the frontend keep its own login/register UI. Identity is now owned by the API itself (ASP.NET Core Identity): the gateway verifies credentials against the local user store, issues self-signed short-lived access tokens, and stores the refresh token only in the secure HttpOnly `auditnode.refresh_token` cookie. JWT bearer validation still protects application APIs. Configuration is supplied through the `Identity:*` keys; secrets are never documented or hard-coded.

After authentication, the `sub`/name-identifier claim selects the actor. Mutable inventory is partitioned by `owner_user_id`; the runtime has no `WorkspaceMiddleware`, `X-Workspace-Id`, or workspace-membership lookup. Owners can delegate labeled subsets through direct Viewer/Editor Label Grants. Anonymous share tokens are revocable Viewer-only capabilities and do not establish an authenticated owner context. Owner-consistent keys, foreign keys, queries, and service authorization form the persistence boundary.

The API currently registers ASP.NET Core Data Protection without a shared multi-instance key ring. Therefore, catalog cursors are protected and validated within the issuing instance but cross-instance cursor portability is not a supported contract. The distributed test suite intentionally protects the database-backed SystemAdmin invariant only. Before adding backend-A cursor to backend-B regression coverage, deployment architecture must explicitly choose either sticky routing for paged requests or a protected shared Data Protection key store; that security/configuration decision is not made by the test suite.

### Canonical aggregate flows

- Server create/update validates canonical IPv4, same-owner datacenter references, and owner-unique IP addresses.
- Application create can persist metadata, labels, and an optional deployment atomically. Deployment updates/migrations target an explicit `PortMappingId`; they never select an arbitrary first mapping.
- Application labels use an `ApplicationLabel` join entity rather than reusing the server-label relationship.
- Topology state is loaded and saved as one `{nodes, edges}` contract. Application nodes are deployment-based and use `PortMappingId` as stable identity. Parent, reference, endpoint, owner, grant, and revision invariants are validated before persistence.
- Dependency synchronization retains the destination deployment mapping and is idempotent, so repeating the same state does not produce duplicate dependencies or new IDs.
- Inventory import validates the complete workbook before one database transaction/save. Any conflict or persistence failure leaves no partial import.

### Error boundary

The API uses Problem Details for unexpected errors and preserves explicit 400/404/409 responses for validation, absence, and conflict. A correlation ID is returned both as `X-Correlation-ID` and in the Problem Details extension. Detailed exceptions are logged without secrets; exception messages are not exposed to clients.

See [API.md](API.md) for routes/contracts and [DATABASE.md](DATABASE.md) for tenant schema and rollout requirements.

---

## Legacy architecture snapshot (superseded)

The sections below describe the earlier implementation and are retained for history. In particular, the old application “find or create/upsert” flow is not the canonical contract above.

## 1. Security Architecture
The system implements a centralized identity management strategy using **Keycloak** (OpenID Connect).

- **Authentication**: JWT Bearer tokens are used to secure API endpoints.
- **Authorization**: The `[Authorize]` attribute is enforced globally on all inventory-related controllers.
- **Token Validation**: The backend validates token signatures, issuer (`http://localhost:8080/realms/AuditNode-Realm`), and expiration.
- **User Context**: User identity is extracted from the `sub` (Subject) or `preferred_username` claims within the JWT for auditing purposes.

## 2. Backend Design Patterns

### Transaction-Based Upsert Pattern
To maintain data integrity and prevent duplicate infrastructure entries, the system employs a "Find or Create" (Upsert) pattern for Application registration.

- **Mechanism**: The `ApplicationRepository` wraps registration logic in a `IDbContextTransaction`.
- **Logic**:
    1. Check for existing `Application` by `AppCode` (Unique Constraint).
    2. If found: Update metadata (Name, OwnerTeam, etc.) and append a new `PortMapping` to the existing entity.
    3. If not found: Create a new `Application` and its initial `PortMapping`.
- **Benefits**: Ensures atomicity and prevents `UniqueConstraintViolation` exceptions while allowing applications to scale across multiple servers.

### Clean Architecture Layers
- **Domain**: Pure POCO entities and enums.
- **Application**: DTOs, Repository Interfaces, Services (Business Logic), and Validation logic (FluentValidation).
- **Infrastructure**: Data access implementation via EF Core and PostgreSQL.
- **API**: ASP.NET Core Controllers and Middleware.

---

## 2. Frontend Topology Design

### Static Resource Inventory Approach
The Topology Map (G1 Inventory) follows a static layout design where infrastructure components are organized hierarchically but represented as a flat searchable inventory.

### React Flow Implementation
The visualization uses **React Flow** with the following customizations:
- **Nested Nodes (Containers)**: Servers act as parent containers for Applications.
- **Grid Auto-layout**: Nodes are automatically positioned on a structured grid to ensure clarity.
- **Interactive Constraints**:
    - **Disabled Dragging**: The layout is fixed to maintain the "Static Inventory" feel.
    - **Z-Index Management**: Precise control over layering to ensure parent nodes (Servers) don't obscure child nodes (Applications).
- **On-Navigate State Synchronization**: When switching between Topology and other views, the application state is synchronized to ensure the view reflects the most current data without unnecessary re-fetches.

---

## 3. Data Flow & Integration
1. **Frontend** requests data from `/api/analytics/topology` or `/api/analytics/dependencies`.
2. **Backend** executes optimized queries against keyless **PostgreSQL Views** (`v_topology_map`, `v_dependency_graph`).
3. **Data** is mapped to DTOs in the Application layer.
4. **React Frontend** renders the infrastructure using React Flow (G3) or nested components (G1).

---

## 4. Quality & Testing Strategy
Regressible behavior is covered through focused tests using the existing xUnit stack. CI separates fast tests from PostgreSQL transaction/concurrency tests so relational checks cannot be silently skipped.

### Test Automation Stack
- **Unit Testing**: xUnit (Standard .NET test runner).
- **Mocking**: Moq (For isolating services and repositories).
- **Assertions**: FluentAssertions (For human-readable test results).
- **Validation Testing**: FluentValidation.TestHelper (For declarative DTO validation checks).
- **Fast Data Tests**: EF Core `InMemoryDatabase` supports quick service/repository feedback where relational semantics are not the subject.
- **PostgreSQL Integration**: Nine tests carry `Category=PostgreSQL` and run against a migrated disposable PostgreSQL 17 database in CI.

### Testing Layers
- **API Controllers**: Mock-based verification of HTTP responses, DTO mapping, and error handling.
- **Application Services**: Core business logic verification with fully mocked dependencies.
- **Infrastructure Repositories**: Fast LINQ/service tests use InMemory; advisory locks, optimistic concurrency, and topology/revoke serialization run in the PostgreSQL category.
- **Security Verification**: Reflection-based tests to ensure every controller is decorated with the `[Authorize]` attribute.
- **Data Validation**: Unit tests for every `AbstractValidator<T>`, ensuring strict adherence to schema constraints (e.g., regex-based IP validation).

### Required backend CI sequence

1. Restore `AuditNode.slnx` in locked mode and build Release binaries.
2. Run the fast suite with `Category!=PostgreSQL` and emit TRX.
3. Apply EF migrations to the disposable PostgreSQL service.
4. Run `Category=PostgreSQL`, emit TRX, and verify that total/executed are greater than zero, every selected test passed, and no test was skipped or not executed.
5. Check for pending EF Core model changes.
6. Publish test/build artifacts. The main-branch image job depends on all verification gates.

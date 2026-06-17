# AuditNode System Architecture

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
The project enforces a **Strict TDD Contract** as per `AGENT.md`, ensuring that all functional code is accompanied by corresponding unit tests.

### Test Automation Stack
- **Unit Testing**: xUnit (Standard .NET test runner).
- **Mocking**: Moq (For isolating services and repositories).
- **Assertions**: FluentAssertions (For human-readable test results).
- **Validation Testing**: FluentValidation.TestHelper (For declarative DTO validation checks).
- **Database Isolation**: EF Core `InMemoryDatabase` provider (For high-speed repository testing without external dependencies).

### Testing Layers
- **API Controllers**: Mock-based verification of HTTP responses, DTO mapping, and error handling.
- **Application Services**: Core business logic verification with fully mocked dependencies.
- **Infrastructure Repositories**: Integrated testing of LINQ queries and transactional logic using an in-memory database.
- **Security Verification**: Reflection-based tests to ensure every controller is decorated with the `[Authorize]` attribute.
- **Data Validation**: Unit tests for every `AbstractValidator<T>`, ensuring strict adherence to schema constraints (e.g., regex-based IP validation).

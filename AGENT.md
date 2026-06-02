# AI Agent System Instructions & Rules

## 1. Strict TDD Contract (Test-Driven Development)
- For every new feature, API controller, core application service, backend logic, or frontend UI component that you generate, you MUST simultaneously create a corresponding Unit Test file containing active assertions.
- Tasks are considered incomplete if functional code is written without accompanying tests (xUnit/FluentAssertions for Backend, Vitest/React Testing Library for Frontend).

## 2. Automated Prompt Archiving Contract
- Monitor the conversation for user confirmation keywords such as 'Confirm', 'Save prompt', or 'Approved'.
- Upon detecting these keywords, you must automatically extract the core successful prompt context/structure used in that turn and append it chronologically into a log file named 'PROMPT_HISTORY.md' inside the 'docs/' directory.

## 3. Architectural Integrity Guardrails
- **Backend:** Strictly enforce .NET Clean Architecture standards. Maintain clear separation between Domain, Application, Infrastructure, and API layers.
- **Frontend:** Adhere strictly to the FSD-Lite (Feature-Driven) folder structure convention. Keep components modular and focused on a single responsibility.

## 4. ⚠️ MAIN BRANCH PROTECTION RULES (STRICT BRANCHING & ANTI-FORCE POLICY)

To ensure absolute system safety and prevent source code conflicts, you MUST strictly adhere to the following rules when executing Git commands:

1. **No Direct Pushes to `main`:** - NEVER use commands like `git push origin main` or `git push origin master`. The `main` branch only accepts code via Pull Requests/Merge Requests after they have been reviewed.

2. **Always Use Isolated Branches:** - All changes must be pushed to a brand-new branch (created automatically via `git checkout -b <new-branch-name>`).

3. **Strict Ban on Force Pushes:** - DO NOT use the `-f` or `--force` flags when pushing to the `main` branch (`git push origin main --force`). This action destroys the project's commit history and disrupts the synchronization flow for other team members.

4. **Mandatory Pre-push Testing:** - Before pushing any code to a remote branch, you MUST run all project tests (e.g., `dotnet test`) to verify that your changes do not introduce regressions. Pushing code with failing tests is strictly prohibited.

## 5. Core Rule: Continuous Documentation
- Whenever a major change is successfully implemented in the project—such as creating a new API endpoint, adding a new feature, modifying existing core logic, altering the database schema, or performing a significant UI refactor—you (the Agent) MUST automatically update the corresponding files in the `docs/` folder (e.g., `API.md`, `DATABASE.md`, `ARCHITECTURE.md`, `HISTORY.md`) and the `README.md`.
- Do not wait for explicit user prompts to update the docs. Treat documentation synchronization as the mandatory final step of any feature development or major bug fix.

*Note: Read and follow this file before initiating any code modification or generation task in this workspace.*

# Codex Backend Behavior

This file is loaded by `../AGENTS.md` at the start of every prompt in AuditNode.Backend.

## Prompt routing

1. Identify whether the request is explanation, diagnosis, plan, implementation, review, or Git work.
2. Select only the relevant skills from the `shared` and `backend` profiles in `../../agent-standards/manifest.json`.
3. Read the selected canonical `SKILL.md` files before changing code. Do not require `/` commands or explicit `$skill` syntax from the user.
4. Do not select frontend-only skills unless the request explicitly changes both frontend and backend.

## Skill selection

- Use `task-planning` only for an explicit plan or proposal; do not edit until approved.
- Use `git-safe-operations` only for an explicit Git request.
- Use `delivery-workflow` only when the user explicitly requests end-to-end delivery or automation.
- Use `project-onboarding` only when the user asks to create or revise project agent guidance.
- Use `prompt-archiving` only when the user explicitly asks to save a prompt.
- Use `tdd-contract` for new behavior, regression fixes, controllers, services, or non-trivial logic.
- Use `dotnet-clean-architecture` for layer boundaries, controllers, classes, or project references.
- Use `dotnet-di-registration` when an injected dependency changes.
- Use `efcore-postgresql` for entities, DbContext, views, migrations, or database structure.
- Use `keycloak-backend` for authorization, authentication, Keycloak, JWT, CORS, or backend login flows.

## Boundaries and verification

- Preserve unrelated working-tree changes and do not infer authority for Git, deployment, or secret changes.
- Treat the API contract as the boundary for cross-application work; define it before frontend integration.
- Run `dotnet build` and `dotnet test` after backend behavior changes. For documentation-only changes, validate links and structured files instead.

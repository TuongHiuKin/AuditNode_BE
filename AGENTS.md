# AuditNode Backend — Codex Guidance

At the beginning of every prompt, read and follow `.codex/BEHAVIOR.md`. It selects applicable skills automatically from `../agent-standards/manifest.json`; do not require the user to invoke a skill explicitly.

For a cross-application task, follow each project's root `AGENTS.md` and treat the API contract as the integration boundary.

- Read `docs/ARCHITECTURE.md` and `docs/API.md` before changing backend architecture or API endpoints.
- Register new application-facing services and repositories in `AuditNode.API/Program.cs`.
- Keep credentials and environment-specific settings out of tracked files.
- Run `dotnet build` and `dotnet test` before reporting backend changes complete.

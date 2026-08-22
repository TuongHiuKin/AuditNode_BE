# AuditNode Backend Rules

Use the portable skills exposed through `skills.json`; their canonical source is `../../agent-standards/skills/`.

- Read `docs/ARCHITECTURE.md` and `docs/API.md` before changing backend architecture or API endpoints.
- Register every new application-facing service or repository implementation in `AuditNode.API/Program.cs`.
- Keep environment-specific credentials out of tracked files; update example configuration instead.
- Run `dotnet build` and `dotnet test` before reporting backend work complete.

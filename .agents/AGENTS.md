# AuditNode.Backend — Project Rules

## Ngữ cảnh Dự án
- **Hệ thống:** Infrastructure Audit & Dependency Management.
- **Tech Stack:** ASP.NET Core 10.0, C# 13, EF Core 10, PostgreSQL, Keycloak JWT, FluentValidation.
- **Kiến trúc:** .NET Clean Architecture (Domain → Application → Infrastructure → API).
- **Test Stack:** xUnit, Moq, FluentAssertions, EF Core InMemory.

## Quy tắc Chung
- Dự án này kế thừa các luật toàn cục từ `F:\Project\AGY_CLI\AGENTS.md` (Git Protection, TDD Contract, Anti-Regression, Continuous Documentation).
- Luôn đọc `docs/ARCHITECTURE.md` và `docs/API.md` trước khi thay đổi kiến trúc hoặc endpoints.
- Khi tạo Service hoặc Repository mới, BẮT BUỘC đăng ký DI trong `AuditNode.API/Program.cs`.
- Lệnh build: `dotnet build`. Lệnh test: `dotnet test`. BẮT BUỘC chạy cả hai trước khi báo hoàn thành.

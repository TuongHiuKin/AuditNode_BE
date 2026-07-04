---
name: clean_architecture_guard
description: Kích hoạt khi tạo mới hoặc chỉnh sửa class trong bất kỳ layer nào (Domain, Application, Infrastructure, API), hoặc khi thêm ProjectReference mới.
---
# .NET Clean Architecture Guard

## Luật Reference Chain (Bất khả xâm phạm)
Hướng phụ thuộc hợp lệ duy nhất:

```
API → Infrastructure → Application → Domain
```

### Các ràng buộc cụ thể:

1. **Domain Layer** (`AuditNode.Domain`):
   - TUYỆT ĐỐI KHÔNG được reference bất kỳ project nào khác.
   - Chỉ chứa: Entities (POCO), Enums, Constants.
   - Cấm sử dụng `using Microsoft.EntityFrameworkCore` hoặc bất kỳ thư viện infrastructure nào.

2. **Application Layer** (`AuditNode.Application`):
   - Chỉ được reference: `AuditNode.Domain`.
   - Chứa: DTOs, Interfaces (Repository & Service contracts), Validators (FluentValidation).
   - Cấm chứa implementation cụ thể (không có class kế thừa Interface ở đây).

3. **Infrastructure Layer** (`AuditNode.Infrastructure`):
   - Được reference: `AuditNode.Domain`, `AuditNode.Application`.
   - Chứa: Repository implementations, Service implementations, `AuditDbContext`, EF Core configurations.
   - Đây là layer DUY NHẤT được phép `using Microsoft.EntityFrameworkCore`.

4. **API Layer** (`AuditNode.API`):
   - Được reference: `AuditNode.Infrastructure`, `AuditNode.Application`.
   - Chứa: Controllers, Middleware, `Program.cs` (DI Registration), `appsettings.json`.
   - Controllers chỉ được inject Interface từ Application layer (ví dụ: `IServerService`), KHÔNG ĐƯỢC inject trực tiếp class từ Infrastructure.

## Quy trình khi tạo tính năng mới
1. Tạo Entity trong `Domain/Entities/`.
2. Tạo DTO và Interface trong `Application/DTOs/` và `Application/Interfaces/`.
3. Tạo Implementation trong `Infrastructure/Services/` hoặc `Infrastructure/Repositories/`.
4. Tạo Controller trong `API/Controllers/`.
5. Đăng ký DI trong `API/Program.cs`.
6. Tạo Unit Test trong `AuditNode.Tests/`.

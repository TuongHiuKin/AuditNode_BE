---
name: di_registration_guard
description: Kích hoạt khi tạo mới Service, Repository, hoặc bất kỳ class nào implement Interface từ Application layer.
---
# Dependency Injection Registration Guard

## Nguyên tắc cốt lõi
Dự án sử dụng DI Registration thủ công trong `AuditNode.API/Program.cs`. Không dùng auto-registration.

## Luật khi tạo Service/Repository mới
1. Tạo Interface trong `AuditNode.Application/Interfaces/` (ví dụ: `INewService.cs`).
2. Tạo Implementation trong `AuditNode.Infrastructure/Services/` hoặc `Infrastructure/Repositories/`.
3. **BẮT BUỘC** đăng ký DI trong `Program.cs` ngay sau khi tạo:
   ```csharp
   builder.Services.AddScoped<INewService, NewService>();
   ```
4. Nếu quên bước này, ứng dụng sẽ crash runtime với lỗi `InvalidOperationException: Unable to resolve service`.

## Quy ước Lifetime
- **Scoped** (`AddScoped`): Mặc định cho tất cả Services và Repositories (1 instance per HTTP request).
- **Singleton** (`AddSingleton`): Chỉ dùng cho configuration objects hoặc cache.
- **Transient** (`AddTransient`): Chỉ dùng cho lightweight utilities không giữ state.

## Kiểm tra sau khi đăng ký
- Chạy `dotnet build` để đảm bảo compile thành công.
- Chạy `dotnet test` để đảm bảo các test hiện tại vẫn xanh (đặc biệt Controller tests dùng Moq sẽ cần mock Interface mới nếu được inject).

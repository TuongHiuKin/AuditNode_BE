---
name: keycloak_auth_guard
description: Kích hoạt khi tạo Controller mới, chỉnh sửa Program.cs, hoặc thay đổi bất kỳ logic liên quan đến Authentication/Authorization.
---
# Keycloak Authentication & Authorization Guard

## Luật bảo vệ Controller
1. **Mọi Controller mới** BẮT BUỘC phải gắn attribute `[Authorize]` ở cấp class.
2. Nếu một endpoint cần public (không cần login), phải dùng `[AllowAnonymous]` trên action cụ thể đó và ghi chú lý do.
3. Dự án hiện có test `SecurityVerificationTests.cs` dùng Reflection để kiểm tra mọi Controller đều có `[Authorize]`. Khi tạo Controller mới, test này sẽ tự động bắt nếu thiếu.

## Luật bảo vệ Pipeline trong Program.cs
Thứ tự middleware trong `Program.cs` là BẤT KHẢ XÂM PHẠM:

```csharp
app.UseCors("AllowReact");
// ... other middleware ...
app.UseAuthentication();   // PHẢI chạy TRƯỚC Authorization
app.UseAuthorization();    // PHẢI chạy SAU Authentication
app.MapControllers();
```

- Cấm di chuyển, hoán đổi, hoặc xóa `UseAuthentication()` / `UseAuthorization()`.
- Cấm thêm middleware xử lý request SAU `MapControllers()`.

## Cấu hình Keycloak
- Authority: Đọc từ `appsettings.json` tại key `Keycloak:Authority`.
- Token Validation: Bật `ValidateIssuer`, `ValidateLifetime`, `ValidateIssuerSigningKey`.
- Khi thay đổi cấu hình Keycloak, PHẢI cập nhật cả `appsettings.json` và `appsettings.example.json`.

---
name: efcore_database_guard
description: Kích hoạt khi tạo Entity mới, chỉnh sửa AuditDbContext.cs, tạo Migration, hoặc thay đổi cấu trúc bảng/view trong PostgreSQL.
---
# EF Core & PostgreSQL Database Guard

## Bảo vệ PostgreSQL Views
Dự án sử dụng **Keyless Entities** để map với PostgreSQL Views. Các View hiện có:
- `v_topology_map` → Entity `TopologyView`
- `v_dependency_graph` → Entity `DependencyView`

### Luật:
1. Keyless Entities (mapped to Views) PHẢI được cấu hình bằng `.HasNoKey()` và `.ToView("view_name")` trong `AuditDbContext`.
2. **TUYỆT ĐỐI KHÔNG** tạo Migration cho Keyless Entities — Views được quản lý bằng SQL scripts, không phải EF Migrations.
3. Không thêm `.HasKey()` vào Keyless Entity — sẽ gây crash runtime.

## Quy tắc tạo Entity mới
1. Entity mới PHẢI đặt trong `AuditNode.Domain/Entities/`.
2. Cấu hình Entity (relationships, indexes, constraints) PHẢI đặt trong `AuditDbContext.OnModelCreating()`.
3. Nếu Entity có UNIQUE constraint, phải khai báo rõ ràng bằng `.HasIndex(...).IsUnique()`.

## Quy tắc Migration
1. Trước khi tạo Migration, chạy `dotnet build` để đảm bảo không có lỗi compile.
2. Sau khi tạo Migration, PHẢI review file Migration sinh ra để đảm bảo không chứa lệnh DROP bất ngờ.
3. Cấm `dotnet ef database update` trên production — chỉ dùng SQL scripts đã được review.

## Connection String
- Đọc từ `appsettings.json` tại key `ConnectionStrings:DefaultConnection`.
- Cấm hard-code connection string trong source code.
- File `appsettings.json` chứa credentials thật PHẢI nằm trong `.gitignore`. Dùng `appsettings.example.json` làm template.

# Tóm tắt Công nghệ Backend - Hệ thống AuditNode

Tài liệu này cung cấp cái nhìn tổng quan về kiến trúc và công nghệ được sử dụng trong phần Backend của dự án AuditNode, phục vụ cho việc hội đồng nhà trường đánh giá.

## 1. Khung nền tảng cốt lõi (Core Framework)
- **Framework**: .NET 10 (Phiên bản mới nhất) Web API.
- **Kiến trúc**: Tuân thủ nghiêm ngặt **Clean Architecture** (Kiến trúc sạch) với việc tách biệt rõ ràng các lớp:
  - **Domain**: Chứa các thực thể (Entities), Enum và logic nghiệp vụ cốt lõi.
  - **Application**: Chứa các giao diện (Interfaces), DTO và logic xử lý yêu cầu (Validators).
  - **Infrastructure**: Triển khai truy cập dữ liệu (EF Core), Repository Pattern và các dịch vụ bên ngoài.
  - **API**: Lớp giao tiếp HTTP, Controller và cấu hình Middleware.

## 2. Tối ưu hóa lớp truy cập dữ liệu (Data Access Layer Optimization)
Hệ thống sử dụng **Entity Framework Core 10** kết hợp với **PostgreSQL** (Npgsql) để quản lý dữ liệu hiệu quả:
- **Loại bỏ độ trễ N+1**: Sử dụng cấu hình ánh xạ điều hướng trực tiếp `Server -> Applications` trong `AuditDbContext`. Điều này cho phép sử dụng `.Include()` và `.ThenInclude()` để tải dữ liệu liên quan trong một câu lệnh SQL duy nhất thay vì thực hiện hàng trăm truy vấn nhỏ.
- **Giảm tải bộ nhớ**: Áp dụng phương thức `.AsNoTracking()` trong các truy vấn chỉ đọc (như `GetTopologyTreeAsync`). Kỹ thuật này bỏ qua cơ chế theo dõi thay đổi (change-tracking) của EF Core, giúp giảm đáng kể mức chiếm dụng RAM của server khi xử lý các tập dữ liệu hạ tầng lớn.

## 3. Khả năng phục hồi và Bảo mật (Resilience & Security)
- **Cơ chế bảo vệ phân trang (Pagination Guards)**: Tại `TopologyController`, tham số `take` được giới hạn cứng ở mức tối đa **100 node**. Điều này ngăn chặn các cuộc tấn công từ chối dịch vụ (DoS) vô tình hoặc hữu ý bằng cách yêu cầu toàn bộ dữ liệu hạ tầng khổng lồ trong một yêu cầu JSON duy nhất.
- **Tích hợp Keycloak OAuth2**:
  - Thực thể `Application` đã được thiết lập trường `OwnerId` (kiểu VARCHAR) để ánh xạ linh hoạt với User ID từ Keycloak hoặc các hệ thống định danh khác.
  - Cấu hình `JwtBearer` sẵn sàng cho việc xác thực tập trung thông qua container Keycloak, đảm bảo an toàn dữ liệu theo tiêu chuẩn công nghiệp.

## 4. Bộ kiểm thử xUnit (xUnit Test Suite)
Hệ thống duy trì độ tin cậy cao thông qua bộ kiểm thử tự động:
- **Công nghệ**: xUnit kết hợp với **Moq** (giả lập repository) và **FluentAssertions** (viết khẳng định dễ đọc).
- **Phạm vi**: 21 ca kiểm thử (test cases) hiện tại đảm bảo:
  - Các hợp đồng dữ liệu JSON (JSON contracts) trả về luôn đúng định dạng phẳng (flat structure).
  - Logic phân trang và giới hạn 100 node hoạt động chính xác.
  - Các ràng buộc thực thể trong Domain luôn được bảo toàn.

---
*Ngày cập nhật: 22/05/2026*
*Phát triển bởi: AI Agent System*

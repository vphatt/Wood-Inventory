# Quản Lý Gỗ (WoodInventory) — Desktop (WPF + SQLite)

Phần mềm ERP quản lý kho gỗ nguyên liệu, chạy **desktop thuần .NET (WPF)**, lưu dữ liệu bằng **SQLite** cục bộ.
**Offline 100%** — không cần internet, không server, không thành phần web (không React/trình duyệt/WebView).

## Phân hệ
- **Bảng Điều Khiển** — KPI tồn kho, biểu đồ theo chủng loại & nhà cung cấp, cảnh báo tồn thấp
- **Phân Loại Gỗ** — danh mục loại gỗ (cha) + phân loại con, gắn nguyên tắc tính m³ (theo quy cách Dày×Rộng×Dài hoặc theo Footage) — không hardcode, mọi nơi khác lấy dropdown từ đây
- **Nhà Cung Cấp** — thông tin NCC (mã số thuế, địa chỉ, tài khoản ngân hàng...)
- **Quản Lý Kiện Gỗ (Lots)** — tra cứu, lọc, xóa kiện gỗ (kiện chỉ được tạo/sửa qua phiếu Nhập Kho, không có form thêm riêng)
- **Báo Giá Gỗ NCC** — mỗi NCC 1 danh sách giá (không còn phiên bản/kích hoạt), field linh hoạt theo range (đủ Dày, hoặc Dày+Rộng+Dài, có/không Xuất xứ...), tự khớp giá cụ thể nhất khi nhập kho
- **Nhập Kho Gỗ** — lập phiếu nhập, tự tra đơn giá từ báo giá NCC theo loại gỗ/phân loại con/xuất xứ/kích thước (cập nhật realtime), khóa giá vốn theo tỷ giá + thuế nhập khẩu khai báo ở phiếu
- **Xuất Kho Gỗ** — xuất theo đơn hàng, hạch toán giá vốn đích danh, khấu trừ tồn

## Cấu trúc mã nguồn (`wpf-src/`)
```
Domain/     Entities + QuotationPriceMatcher + WoodVolumeCalculator (nghiệp vụ thuần)
Data/       AppDbContext (EF Core + SQLite) + DbSeeder + AppState
Helpers/    Fmt (format số/tiền/ngày vi-VN + parse ngược) + GridLayoutStore (nhớ bố cục cột) + RowNumberConverter (STT)
Views/      8 màn hình WPF chính (Dashboard, Phân Loại Gỗ, Nhà Cung Cấp, Lots, Quotations, Receipts, Issues, DotNet)
            + 2 trang con drill-down (chi tiết báo giá NCC, phân loại con)
App.xaml    Bảng màu + style dùng chung (slate/blue/emerald/rose) + thanh cuộn mảnh riêng theo theme
MainWindow  Sidebar + dải tab động + breadcrumb + thanh trạng thái
```

Dữ liệu lưu tại: `%APPDATA%\WoodInventory\woodinventory.db` (tự tạo + seed dữ liệu mẫu lần chạy đầu).

## Yêu cầu
- .NET 8 SDK
- Inno Setup 6 (chỉ cần khi đóng gói installer)

## Chạy khi phát triển
```powershell
dotnet run --project wpf-src\WoodInventory.csproj
```
Mở thẳng một phân hệ: thêm `--module lots` (hoặc `dashboard`, `quotations`, `receipts`, `issues`, `dotnet`).

## Đóng gói bản phát hành (1 lệnh)
```powershell
powershell -ExecutionPolicy Bypass -File .\build-wpf-desktop.ps1
```
Kết quả:
- `build\desktop-app\WoodInventory.exe` — 1 file exe self-contained (máy đích không cần cài .NET)
- `build\installer\WoodInventory-Setup.exe` — file cài đặt Windows hoàn chỉnh

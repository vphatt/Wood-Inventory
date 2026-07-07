# TimberFlow ERP — Desktop (WPF + SQLite)

Phần mềm ERP quản lý kho gỗ nguyên liệu, chạy **desktop thuần .NET (WPF)**, lưu dữ liệu bằng **SQLite** cục bộ.
**Offline 100%** — không cần internet, không server, không thành phần web (không React/trình duyệt/WebView).

## Phân hệ
- **Bảng Điều Khiển** — KPI tồn kho, biểu đồ theo chủng loại & nhà cung cấp, cảnh báo tồn thấp
- **Quản Lý Kiện Gỗ (Lots)** — tra cứu, khai báo kiện, truy xuất nguồn gốc & lịch sử xuất
- **Báo Giá Gỗ NCC** — quản lý báo giá theo phiên bản, kích hoạt/tạm dừng
- **Nhập Kho Gỗ** — lập phiếu nhập, tự tra đơn giá từ báo giá đang kích hoạt, khóa giá vốn
- **Xuất Kho Gỗ** — xuất theo đơn hàng, hạch toán giá vốn đích danh, khấu trừ tồn

## Cấu trúc mã nguồn (`wpf-src/`)
```
Domain/     Entities + WoodVolumeCalculator (nghiệp vụ thuần)
Data/       AppDbContext (EF Core + SQLite) + DbSeeder + AppState
Views/      6 màn hình WPF (Dashboard, Lots, Quotations, Receipts, Issues, DotNet)
App.xaml    Bảng màu + style dùng chung (slate/blue/emerald/rose)
MainWindow  Sidebar + dải tab động + breadcrumb + thanh trạng thái
```

Dữ liệu lưu tại: `%APPDATA%\TimberFlowDesktop\timberflow.db` (tự tạo + seed dữ liệu mẫu lần chạy đầu).

## Yêu cầu
- .NET 8 SDK
- Inno Setup 6 (chỉ cần khi đóng gói installer)

## Chạy khi phát triển
```powershell
dotnet run --project wpf-src\TimberFlowDesktop.csproj
```
Mở thẳng một phân hệ: thêm `--module lots` (hoặc `dashboard`, `quotations`, `receipts`, `issues`, `dotnet`).

## Đóng gói bản phát hành (1 lệnh)
```powershell
powershell -ExecutionPolicy Bypass -File .\build-wpf-desktop.ps1
```
Kết quả:
- `build\desktop-app\TimberFlowDesktop.exe` — 1 file exe self-contained (máy đích không cần cài .NET)
- `build\installer\TimberFlowDesktop-Setup.exe` — file cài đặt Windows hoàn chỉnh

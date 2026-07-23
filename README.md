# Quản Lý Gỗ (WoodInventory) — Desktop (WPF + SQLite)

Phần mềm ERP quản lý kho gỗ nguyên liệu, chạy **desktop thuần .NET (WPF)**, lưu dữ liệu bằng **SQLite** cục bộ.
**Offline 100%** — không cần internet, không server, không thành phần web (không React/trình duyệt/WebView).
Giao diện **đa ngôn ngữ: Tiếng Việt / 中文 (giản thể)**, đổi trong Cài Đặt và áp dụng ngay không cần khởi động lại.

## Phân hệ
- **Bảng Điều Khiển** — KPI tồn kho, biểu đồ theo chủng loại & nhà cung cấp, cảnh báo tồn thấp
- **Phân Loại Gỗ** — danh mục loại gỗ (cha) + phân loại con, gắn nguyên tắc tính m³ (theo quy cách Dày×Rộng×Dài hoặc theo Footage) — không hardcode, mọi nơi khác lấy dropdown từ đây
- **Nhà Cung Cấp** — thông tin NCC (mã số thuế, địa chỉ, tài khoản ngân hàng...)
- **Tồn Kho** — bảng toàn bộ kiện gỗ đang tồn (NCC, hóa đơn, ngày nhập, phiếu giao hàng, mã kiện, loại/phân loại gỗ, kích thước, số lượng tồn, thể tích); bấm icon xem để mở panel truy xuất chi tiết (thông tin chung, tài chính & thuế VAT, chứng từ nguồn gốc). Kiện chỉ được tạo/sửa qua phiếu Nhập Kho, không có form thêm riêng
- **Báo Giá Gỗ** — mỗi NCC 1 danh sách giá (mở từ **Nhà Cung Cấp → "Xem báo giá"**, không có tab riêng), mỗi dòng giá có mã riêng `BG###-###`, field linh hoạt (Loại · Phân loại con · Xuất xứ · Chất lượng · kích thước theo 3 chế độ **Đơn lẻ / Khoảng / Nhiều giá trị**), đơn vị tiền **USD hoặc VND**, tự khớp giá cụ thể nhất khi nhập kho; có **lịch sử điều chỉnh giá** (bắt buộc ghi lý do khi đổi giá)
- **Nhập Kho Gỗ** — lập phiếu nhập, tự tra đơn giá từ báo giá NCC theo loại gỗ/phân loại con/xuất xứ/chất lượng/kích thước (cập nhật realtime, có gợi ý lọc chéo giữa các ô kích thước), khai đơn vị tiền tệ + tỷ giá + thuế VAT + bảng kê lâm sản ở phiếu; kèm trang **Bảng Tổng Chi Tiết Nhập Kho** liệt kê toàn bộ kiện đã nhập với số lượng/thể tích/giá trị tại thời điểm nhập
- **Xuất Kho Gỗ** — xuất theo đơn hàng, hạch toán giá vốn đích danh, khấu trừ tồn; sửa/xóa phiếu xuất tự **hoàn trả** lại tồn kho
- **Cài Đặt** — ngôn ngữ giao diện (Việt/Trung) + cấu hình mặc định (tỷ giá, % thuế VAT, số lẻ m³, định mức cảnh báo tồn thấp, tên công ty...)

## Cấu trúc mã nguồn (`wpf-src/`)
```
Domain/     Entities + QuotationPriceMatcher + WoodVolumeCalculator (nghiệp vụ thuần)
Data/       AppDbContext (EF Core + SQLite) + DbSeeder + AppState (mọi thao tác ghi dữ liệu)
Helpers/    Fmt (format số/tiền/ngày vi-VN + parse ngược) · GridLayoutStore (nhớ thứ tự cột)
            · RowNumbering (STT đúng khi ảo hóa) · GridPairSync (đồng bộ cặp bảng)
            · LanguageService/Lang/LocExtension (đa ngôn ngữ) · AppDialog · UiScroll
Resources/  Lang/vi.json, Lang/zh-Hans.json — bảng dịch giao diện
Views/      màn hình WPF chính (Dashboard, Phân Loại Gỗ, Nhà Cung Cấp, Nhập Kho, Tồn Kho, Xuất Kho, Cài Đặt)
            + trang con drill-down (chi tiết báo giá NCC, lịch sử giá, phân loại con, Bảng Tổng Chi Tiết Nhập Kho)
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
Mở thẳng một phân hệ: thêm `--module lots` (các giá trị hợp lệ: `dashboard`, `suppliers`, `categories`, `receipts`, `lots`, `issues`, `settings`).

Hot reload khi sửa code:
```powershell
dotnet watch --project wpf-src\WoodInventory.csproj
```

## Đóng gói bản phát hành (1 lệnh)
```powershell
powershell -ExecutionPolicy Bypass -File .\build-wpf-desktop.ps1
```
Kết quả:
- `build\desktop-app\WoodInventory.exe` — 1 file exe self-contained (máy đích không cần cài .NET)
- `build\installer\WoodInventory-Setup.exe` — file cài đặt Windows hoàn chỉnh

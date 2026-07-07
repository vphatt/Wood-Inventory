# CLAUDE.md — TimberFlow ERP Desktop

## Cách xưng hô
Nói chuyện với dev bằng **mày-tao**. Tao là Claude, mày là dev. Xưng hô kiểu bạn bè, thẳng, gọn — đừng khách sáo "bạn/mình/quý khách" gì hết. Tiếng Việt là chính.

## Project là cái gì
ERP quản lý kho gỗ nguyên liệu. **Desktop thuần WPF (.NET 8) + SQLite. Offline 100%.**
KHÔNG có web ở đây — không React, không ASP.NET, không server, không trình duyệt, không WebView. Trước đây từng có bản web nhưng đã xoá sạch rồi, đừng có dựng lại. Nếu mày thấy tao định thêm dependency web hay tách backend/API thì tao sai — đây là app desktop 1 tiến trình.

## Chạy & build (chạy tại thư mục gốc, PowerShell)
```powershell
# Chạy debug
dotnet run --project wpf-src\TimberFlowDesktop.csproj

# Mở thẳng 1 phân hệ: dashboard | lots | quotations | receipts | issues | dotnet
dotnet run --project wpf-src\TimberFlowDesktop.csproj -- --module lots

# Hot reload khi sửa code
dotnet watch --project wpf-src\TimberFlowDesktop.csproj

# Đóng gói phát hành (exe self-contained + installer Inno Setup) — 1 lệnh
powershell -ExecutionPolicy Bypass -File .\build-wpf-desktop.ps1
```
Ra: `build\desktop-app\TimberFlowDesktop.exe` (self-contained, máy đích không cần .NET) và `build\installer\TimberFlowDesktop-Setup.exe`.

## Kiến trúc (`wpf-src/`)
```
Domain/     Entities.cs          — POCO thuần (WoodCategory, Supplier{+TaxCode,BankAccount}, WoodLot, WoodQuotation, WarehouseReceipt/Issue, Order...) + enum VolumeRule
            WoodVolumeCalculator — TOÀN BỘ công thức nghiệp vụ nằm ở đây, static, dùng chung
Data/       AppDbContext.cs      — EF Core + SQLite, cấu hình quan hệ/index
            DbSeeder.cs          — seed dữ liệu mẫu, CHỈ seed khi bảng rỗng
            AppState.cs          — kho dữ liệu in-memory dùng chung + mọi nghiệp vụ ghi
Helpers/    Fmt.cs               — format số/tiền/ngày
Views/      *View.xaml(.cs)      — 8 màn hình (Dashboard, WoodCategories, Suppliers, Lots, Quotations, Receipts, Issues, DotNet)
App.xaml    — bảng màu + toàn bộ Style dùng chung (slate/blue/emerald/rose theo Tailwind)
MainWindow  — sidebar + dải tab động + breadcrumb + status bar
```

## Phân loại gỗ & Nguyên tắc tính m³ (QUAN TRỌNG — NHỚ KỸ)
Loại gỗ KHÔNG còn hardcode nữa — quản lý động trong tab **Phân Loại Gỗ** (`WoodCategoriesView`), lưu ở bảng `WoodCategories`.
Mỗi loại gỗ gắn 1 `VolumeRule`:
- **`BySpecification`** (Theo quy cách Dày × Rộng × Dài): khi nhập/xuất loại gỗ này **BẮT BUỘC nhập đủ 3 thông số Dày + Rộng + Dài**.
- **`ByFootage`** (Theo Footage): khi nhập/xuất **CHỈ bắt buộc Dày + Footage** (không cần Rộng/Dài).

Tra rule bằng `AppState.GetVolumeRule(tênLoạiGỗ)` (fallback đoán theo tên nếu chưa có trong danh mục). Danh sách tên loại gỗ cho dropdown: `AppState.CategoryNames`. Đã áp dụng xong ở `ReceiptsView` (Nhập Kho) và `LotsView` (hiển thị) — dropdown đọc động, ẩn/hiện Footage vs Rộng/Dài + validate bắt buộc theo rule.

Với gỗ nhóm **ByFootage**, số mm chính xác không có ý nghĩa tính toán (công thức chỉ dùng Footage) nên:
- Độ dày cho phép nhập ký hiệu ngành gỗ Mỹ: `1"`, `4/4"`, `5/4"`... — parse bằng `WoodVolumeCalculator.ParseFootageThicknessMm(text)`.
- Chiều dài cho phép mô tả nhiều giá trị (vd `132"144"`) lưu ở `WoodLot.LengthNote` (chỉ hiển thị, không tính toán).

## Quy tắc & pattern PHẢI theo
- **Nghiệp vụ tính toán chỉ có 1 chỗ:** `WoodVolumeCalculator`. Đừng bao giờ nhân/chia công thức thể tích hay giá vốn rải rác trong View. Công thức: rule ByFootage = `(Footage/1000)*2.36`; BySpecification = `Dài*Rộng*Dày*SốLượng/1e9`. Giá vốn VND/m³ = `USD * tỷ giá * (1 + thuế%/100)`.
- **Mọi ghi dữ liệu đi qua `AppState`** (AddCategory/UpdateCategory/DeleteCategory, AddSupplier/UpdateSupplier/DeleteSupplier, AddReceipt, AddIssue, AddQuotationItem/UpdateQuotationItem/DeleteQuotationItem/DeleteQuotation, DeleteLot). Kiện gỗ (`WoodLot`) CHỈ được tạo qua `AddReceipt` (phiếu Nhập Kho) — `LotsView` (Quản Lý Kiện Gỗ) thuần xem/lọc/xóa, không có add/edit. Lưu ý: `UpdateCategory` khi đổi TÊN loại gỗ sẽ cascade cập nhật `WoodType` của mọi WoodLot + QuotationItem đang dùng tên cũ. `DeleteSupplier`/`DeleteCategory` chặn xóa nếu đang được tham chiếu. Nó tự mở `AppDbContext`, `SaveChanges`, rồi `Reload()` + bắn event `Changed`. View đừng tự đụng DbContext để ghi.
- **View nào cần tự làm mới thì implement `IModuleView.RefreshView()`.** MainWindow gọi lại khi đổi tab / khi `AppState.Changed`.
- **UI build bằng code-behind là chủ đạo** (thẻ, badge, form... dựng bằng C# trong `Build...()`), XAML chỉ dựng khung + style. Giữ nguyên phong cách đó cho nhất quán, đừng nửa nạc nửa mỡ.
- **Bảng dữ liệu = `DataGrid` (style `DataTable` trong App.xaml)**, KHÔNG dựng tay bằng Grid/ItemsControl nữa. Cho sẵn sort/kéo đổi thứ tự cột/kéo giãn cột/border dọc. Pattern: mỗi View có 1 class `XxxRow` (bọc entity + expose property cho binding), `List<XxxRow> _rows` + `ICollectionView _view = CollectionViewSource.GetDefaultView(_rows)` với `_view.Filter = FilterPredicate`; cột định nghĩa trong XAML (DataGridTextColumn dùng `ElementStyle`; ô đặc biệt/badge/nút thao tác dùng `DataGridTemplateColumn` + `Click` handler lấy `DataContext` ra `XxxRow`). Search/filter = TextBox + ComboBox gọi `_view.Refresh()`. **Đặt tên DataGrid KHÁC "Grid"** (vd `HistoryGrid`, `LotGrid`) vì code-behind còn dùng `Grid.SetColumn(...)` — trùng tên sẽ lỗi biên dịch.
- **Lưu bố cục cột (thứ tự + độ rộng):** gọi `Helpers.GridLayoutStore.Attach(TheGrid, "khóa")` trong constructor sau khi cột đã tạo. Nó tự đọc/ghi `%APPDATA%\TimberFlowDesktop\grid-layout.json` (per-user) → giữ nguyên khi đổi tab hay thoát/mở lại app. Mỗi bảng 1 khóa riêng (categories/suppliers/receipts/issues/lots/quotation-suppliers/quotation-items). Bảng mới nhớ Attach với khóa mới. Khóa cột định danh theo Header nên đừng trùng Header trong cùng bảng.
- **Style lấy từ App.xaml** qua `FindResource(...)` / `{StaticResource ...}`. Thêm màu/brush mới thì khai báo trong App.xaml, đừng hard-code hex trong View trừ khi thật sự cần alpha đặc biệt.
- **Form add/edit giãn FULL chiều ngang, chia đều** — KHÔNG đặt `MaxWidth`/`HorizontalAlignment="Left"` trên `AddFormPanel`. Các field xếp trên Grid cột `*` để tự chia đều theo bề rộng. Bấm Add đẩy bảng detail xuống (form nằm trên bảng trong cùng StackPanel).
- **Xem chi tiết = dùng lại form add/edit ở chế độ read-only** (không mở dialog riêng). Cột thao tác chỉ có **Xem (mắt E7B3) + Xóa (thùng rác E74D)**, KHÔNG có nút Edit. 3 chế độ dùng field `_mode` = `add|view|edit`: `EnterViewMode` điền dữ liệu + `SetReadOnly(true)` + đổi nút thành "Chỉnh sửa"; bấm nút đó gọi `EnterEditMode` (mở khóa + nút "Cập nhật"); `BtnSave_Click` đầu hàm `if (_mode=="view") { EnterEditMode(); return; }`. Nút "Thêm mới" khi đang xem/sửa thì chuyển thẳng sang add mode (clear), chỉ đóng form khi đang ở add mode. Xem `WoodCategoriesView`/`SuppliersView`.
- **Báo giá = 1 danh sách / NCC, KHÔNG còn phiên bản (Version/IsActive bỏ hẳn).** `QuotationsView` chỉ là bảng NCC (1 dòng/NCC: `AppState.QuotationItemCount(id)` + `FindQuotation(id).EffectiveDate`). Bấm "Xem báo giá" **điều hướng sang trang chi tiết trong CÙNG tab** (không expand, không mở tab mới): `QuotationsView` chứa `ScrollViewer ListRoot` + `ContentControl DetailHost`; `OpenDetail(sup)` gắn `new QuotationDetailView(sup, BackToList)` vào DetailHost, ẩn ListRoot. `QuotationDetailView` là bảng mục giá của 1 NCC + search/filter + CRUD đầy đủ (dùng `AddQuotationItem/UpdateQuotationItem/DeleteQuotationItem`, get-or-create quotation theo `SupplierId`). Về danh sách qua `BackToList()`.
- **Breadcrumb drill-down trong 1 tab:** gọi `(Window.GetWindow(this) as MainWindow)?.SetBreadcrumbDetail(tênChiTiết, onBack)` khi vào trang con → hiện thêm cấp `… / Báo Giá Gỗ / Tên NCC` và biến "Báo Giá Gỗ" thành link xanh gọi `onBack`. `SetBreadcrumbDetail(null)` khi quay lại. `ActivateTab` tự reset về null nên khi đổi tab breadcrumb sạch; view nào đang ở trang con thì re-apply trong `RefreshView()`.
- **Validate nhập thiếu = cảnh báo inline dưới field, KHÔNG MessageBox.** Mỗi field bắt buộc có `<TextBlock Style="{StaticResource FieldWarn}">` (đỏ, ẩn sẵn) ngay dưới; TextBox gắn `Tag="{Binding ElementName=WXxx}"` + `TextChanged="Field_Changed"` để tự ẩn cảnh báo khi user gõ. `BtnSave_Click` gọi `ClearWarnings()` rồi `ShowWarn(WXxx, "...")` cho field trống; lỗi nghiệp vụ từ AppState (trùng tên/code) cũng `ShowWarn` dưới field liên quan thay vì dialog. MessageBox chỉ giữ cho lỗi xóa (không có field để gắn).
- **Text hiển thị cần cho copy**: dùng style `CopyableText` (TextBox read-only, không viền, nền trong) thay `TextBlock` — WPF `TextBlock` KHÔNG bôi đen/copy được.

## Bẫy đã dính (đừng dẫm lại)
- **Đổi schema DB (thêm bảng/cột) mà đang dùng `EnsureCreated`:** `EnsureCreated` KHÔNG cập nhật DB đã tồn tại ở `%APPDATA%` → bảng/cột mới thiếu, query crash "no such table/column". Xử lý trong `DbSeeder`: thêm BẢNG mới → raw SQL `CREATE TABLE IF NOT EXISTS` (xem `EnsureWoodCategoriesTable`); thêm CỘT vào bảng cũ → check `PRAGMA table_info` rồi `ALTER TABLE ADD COLUMN` (xem `EnsureSupplierColumns`, vì SQLite không có ADD COLUMN IF NOT EXISTS). Seed mới chỉ chạy khi bảng rỗng nên bản ghi cũ sẽ có cột mới = NULL (đúng, không đè dữ liệu). Hoặc bảo dev xóa `%APPDATA%\TimberFlowDesktop` để tạo lại. Project KHÔNG dùng EF Migrations.
- **Sửa mảng `NavItems` trong MainWindow:** các dòng có sẵn chứa ký tự icon PUA khó match bằng Edit → chèn/sửa bằng PowerShell (set glyph bằng `[char]0xE71D`).
- **Icon MDL2:** dùng `FontFamily="Segoe MDL2 Assets"` + ký tự Private-Use (U+E000–F8FF). Trong **XAML phải viết escape `&#xE710;`**, KHÔNG dán ký tự glyph thẳng — tooling/linter nuốt mất ký tự PUA làm icon biến mất. Trong code-behind C# thì dùng `""`.
- **`OutputType=WinExe`** để ẩn cửa sổ console (đây là app GUI). Đổi lại thành Exe là hiện console đen.
- **DB ở `%APPDATA%\TimberFlowDesktop\timberflow.db`** — cố tình để chỗ ghi được, vì cài vào Program Files là thư mục chỉ-đọc. Đừng đổi về thư mục app.
- **Build bị khoá file** `TimberFlowDesktop.exe is locked`: app đang chạy. Đóng cửa sổ hoặc `Get-Process TimberFlowDesktop | Stop-Process -Force` rồi build lại.
- **Reset dữ liệu về mẫu:** `Remove-Item "$env:APPDATA\TimberFlowDesktop" -Recurse -Force` rồi chạy lại (DbSeeder tự seed lại).
- **Chụp màn hình kiểm chứng UI:** phải `SetProcessDPIAware()` trước khi CopyFromScreen, không thì ảnh bị lệch/mờ do DPI. App khai báo PerMonitorV2 trong `app.manifest`.
- **Format số:** `Fmt` dùng `InvariantCulture` cho m³/USD (dấu chấm thập phân) nhưng `vi-VN` cho tiền VND (`392.286.085 ₫`). Muốn khớp bản gốc thì giữ đúng vậy.

## Khi kiểm chứng thay đổi UI
Build → chạy exe → chụp màn hình → đọc lại ảnh để xác nhận, đừng chỉ tin "build succeeded". Trước giờ tao toàn làm vậy và bắt được lỗi (icon mất, chart lệch...).

## Đừng làm
- Đừng seed đè dữ liệu người dùng (DbSeeder chỉ chạy khi rỗng — giữ nguyên).
- Đừng commit / push nếu mày không bảo. Đây không phải git repo sẵn.
- Đừng thêm lại thứ gì thuộc bản web.

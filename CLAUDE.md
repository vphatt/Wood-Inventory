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
Domain/     Entities.cs          — POCO thuần (WoodCategory + WoodSubCategory(phân loại con), Supplier{+TaxCode,BankAccount}, WoodLot{+WoodSubType,ThicknessNote,LengthNote,Origin}, WoodQuotation, QuotationItem{+WoodSubType}, WarehouseReceipt/Issue, Order...) + enum VolumeRule
            QuotationPriceMatcher — khớp giá 1 kiện với báo giá NCC (specificity kiểu firewall rule; con trước, fallback cha)
            WoodVolumeCalculator — TOÀN BỘ công thức nghiệp vụ nằm ở đây, static, dùng chung
Data/       AppDbContext.cs      — EF Core + SQLite, cấu hình quan hệ/index
            DbSeeder.cs          — seed dữ liệu mẫu, CHỈ seed khi bảng rỗng
            AppState.cs          — kho dữ liệu in-memory dùng chung + mọi nghiệp vụ ghi
Helpers/    Fmt.cs               — format số/tiền/ngày
Views/      *View.xaml(.cs)      — 8 tab chính (Dashboard, WoodCategories, Suppliers, Lots, Quotations, Receipts, Issues, DotNet)
                                   + 2 trang con drill-down: QuotationDetailView, WoodSubCategoriesView
App.xaml    — bảng màu + toàn bộ Style dùng chung (slate/blue/emerald/rose theo Tailwind)
MainWindow  — sidebar + dải tab động + breadcrumb + status bar
```

## Phân loại gỗ 2 cấp & Nguyên tắc tính m³ (QUAN TRỌNG — NHỚ KỸ)
Loại gỗ KHÔNG hardcode — quản lý **động, 2 cấp** trong tab **Phân Loại Gỗ**:
- **Cấp 1 (cha)** = `WoodCategory` (bảng `WoodCategories`): tên loại gỗ + `VolumeRule`.
- **Cấp 2 (con)** = `WoodSubCategory` (bảng `WoodSubCategories`, FK `CategoryId`→cha, ON DELETE CASCADE): vd Gỗ Thông → "Thông trắng"/"Thông vàng", Gỗ Dương → "1 com"/"2 com". Rule **kế thừa từ cha**, không lưu riêng.
- UI **drill-down như Báo Giá**: `WoodCategoriesView` (bảng cha + cột "PHÂN LOẠI CON" hiện số, host `ListRoot`+`DetailHost`) → bấm mở `WoodSubCategoriesView(category, back)` trong CÙNG tab (breadcrumb `Phân Loại Gỗ / Tên cha`).

Mỗi loại gỗ **cha** gắn 1 `VolumeRule`:
- **`BySpecification`** (Dày × Rộng × Dài): nhập/xuất **bắt buộc Dày + Rộng + Dài**.
- **`ByFootage`** (Theo Footage): nhập/xuất **bắt buộc Dày + Footage** (không cần Rộng).

Tra cứu qua `AppState`: rule = `GetVolumeRule(tênCha)` (fallback đoán theo tên nếu chưa có); dropdown loại gỗ = `CategoryNames`; phân loại con nối tầng = `SubNamesOf(tênCha)`; cha có con chưa = `CategoryHasSubs(tênCha)`.

**`WoodLot` + `QuotationItem` lưu cả `WoodType` (cha) + `WoodSubType` (con, nullable).** Rule vẫn tra theo `WoodType` (cha) nên `WoodVolumeCalculator` KHÔNG đổi. Khớp giá: `QuotationPriceMatcher.FindBestMatch(..., woodSubType)` **ưu tiên khớp đúng con, không có thì fallback dòng giá cấp cha** (mọi field NULL trên dòng giá = wildcard; con để trống = áp mọi con). Ở **Nhập Kho** phân loại con **BẮT BUỘC nếu cha có con**; ở **Báo Giá** để trống là hợp lệ (chính là giá cấp cha).

**Gỗ nhóm `ByFootage`** — mm chính xác vô nghĩa (công thức chỉ dùng Footage) nên độ dày + độ dài chỉ mô tả:
- Độ dày nhập ký hiệu inch: `1"`, `4/4"`, `5/4"`... — parse bằng `WoodVolumeCalculator.ParseFootageThicknessMm(text)`; giữ ký hiệu gốc ở `WoodLot.ThicknessNote`.
- Độ dài nhập nhiều giá trị inch (vd `96"108"120"`) lưu ở `WoodLot.LengthNote`. Cả hai chỉ hiển thị, không tính m³.

## Quy tắc & pattern PHẢI theo
- **Nghiệp vụ tính toán chỉ có 1 chỗ:** `WoodVolumeCalculator`. Đừng bao giờ nhân/chia công thức thể tích hay giá vốn rải rác trong View. Công thức: rule ByFootage = `(Footage/1000)*2.36`; BySpecification = `Dài*Rộng*Dày*SốLượng/1e9`. Giá vốn VND/m³ = `USD * tỷ giá * (1 + thuế%/100)`.
- **Mọi ghi dữ liệu đi qua `AppState`** (AddCategory/UpdateCategory/DeleteCategory, AddSubCategory/UpdateSubCategory/DeleteSubCategory, AddSupplier/UpdateSupplier/DeleteSupplier, AddReceipt/**UpdateReceipt**/**DeleteReceipt**, AddIssue, AddQuotationItem/UpdateQuotationItem/DeleteQuotationItem/DeleteQuotation, DeleteLot). Kiện gỗ (`WoodLot`) CHỈ được tạo/sửa/xóa qua phiếu Nhập Kho (`AddReceipt`/`UpdateReceipt`/`DeleteReceipt`) — `LotsView` (Quản Lý Kiện Gỗ) thuần xem/lọc/xóa. Cascade & chặn: `UpdateCategory` đổi TÊN cha → cascade `WoodType` mọi WoodLot+QuotationItem; `UpdateSubCategory` đổi tên con → cascade `WoodSubType`; `DeleteSupplier`/`DeleteCategory`/`DeleteSubCategory` chặn nếu đang được tham chiếu; `UpdateReceipt`/`DeleteReceipt` chặn nếu phiếu có kiện **đã phát sinh xuất kho** (giữ nhất quán tồn kho), `UpdateReceipt` xóa hết kiện cũ rồi thêm lại (2 lần `SaveChanges` để tái dùng mã kiện không trùng khóa). Mỗi hàm tự mở `AppDbContext`, `SaveChanges`, rồi `Reload()` + bắn event `Changed`. View đừng tự đụng DbContext để ghi.
- **View nào cần tự làm mới thì implement `IModuleView.RefreshView()`.** MainWindow gọi lại khi đổi tab / khi `AppState.Changed`.
- **UI build bằng code-behind là chủ đạo** (thẻ, badge, form... dựng bằng C# trong `Build...()`), XAML chỉ dựng khung + style. Giữ nguyên phong cách đó cho nhất quán, đừng nửa nạc nửa mỡ.
- **Bảng dữ liệu = `DataGrid` (style `DataTable` trong App.xaml)**, KHÔNG dựng tay bằng Grid/ItemsControl nữa. Cho sẵn sort/kéo đổi thứ tự cột/kéo giãn cột/border dọc. Pattern: mỗi View có 1 class `XxxRow` (bọc entity + expose property cho binding), `List<XxxRow> _rows` + `ICollectionView _view = CollectionViewSource.GetDefaultView(_rows)` với `_view.Filter = FilterPredicate`; cột định nghĩa trong XAML (DataGridTextColumn dùng `ElementStyle`; ô đặc biệt/badge/nút thao tác dùng `DataGridTemplateColumn` + `Click` handler lấy `DataContext` ra `XxxRow`). Search/filter = TextBox + ComboBox gọi `_view.Refresh()`. **Đặt tên DataGrid KHÁC "Grid"** (vd `HistoryGrid`, `LotGrid`) vì code-behind còn dùng `Grid.SetColumn(...)` — trùng tên sẽ lỗi biên dịch.
- **Bảng khai kiện gỗ trong form Nhập Kho là NGOẠI LỆ** (không dùng DataGrid): mỗi dòng kiện là 1 `Grid` dựng tay trong `BuildLotRow` (ô nhập động + tính m³/giá vốn realtime + ẩn/hiện cột theo rule). Header (XAML) và row (code-behind) phải KHỚP số cột + độ rộng. Thứ tự cột: Mã kiện · Loại gỗ(cha) · Phân loại(con) · **Xuất xứ** · Dày · Rộng · Dài · Footage · Số lượng · Thể tích m³ · Giá USD · Giá vốn VND · Xóa. `ApplyRuleVisibility()`: gỗ **quy cách** ẩn cột Footage; gỗ **footage** ẩn cột Rộng (cột Dài: quy cách = mm dùng tính m³, footage = ký hiệu inch mô tả). `DraftLot` mới để **TRỐNG hết mọi field** (loại gỗ = placeholder "— Chọn loại gỗ —"; validate bắt buộc Mã kiện + Loại gỗ khi lưu). Cột "Xuất xứ" = `WoodLot.Origin` (THAY cho Grade cũ) — matcher khớp giá theo Origin, không còn theo Grade.
- **Lưu bố cục cột (thứ tự + độ rộng):** gọi `Helpers.GridLayoutStore.Attach(TheGrid, "khóa")` trong constructor sau khi cột đã tạo. Nó tự đọc/ghi `%APPDATA%\TimberFlowDesktop\grid-layout.json` (per-user) → giữ nguyên khi đổi tab hay thoát/mở lại app. Mỗi bảng 1 khóa riêng (categories/suppliers/receipts/issues/lots/quotation-suppliers/quotation-items). Bảng mới nhớ Attach với khóa mới. Khóa cột định danh theo Header nên đừng trùng Header trong cùng bảng.
- **Style lấy từ App.xaml** qua `FindResource(...)` / `{StaticResource ...}`. Thêm màu/brush mới thì khai báo trong App.xaml, đừng hard-code hex trong View trừ khi thật sự cần alpha đặc biệt.
- **Form add/edit giãn FULL chiều ngang, chia đều** — KHÔNG đặt `MaxWidth`/`HorizontalAlignment="Left"` trên `AddFormPanel`. Các field xếp trên Grid cột `*` để tự chia đều theo bề rộng. Bấm Add đẩy bảng detail xuống (form nằm trên bảng trong cùng StackPanel).
- **Xem chi tiết = dùng lại form add/edit ở chế độ read-only** (không mở dialog riêng). 3 chế độ dùng field `_mode` = `add|view|edit`: `EnterViewMode` điền dữ liệu + `SetReadOnly(true)` + đổi nút chính thành "Chỉnh sửa"; bấm nút đó gọi `EnterEditMode` (mở khóa + nút "Cập nhật"); `BtnSave_Click` đầu hàm `if (_mode=="view") { EnterEditMode(); return; }`. Nút "Thêm mới" khi đang xem/sửa thì chuyển thẳng sang add mode (clear), chỉ đóng form khi đang ở add mode. Xem `WoodCategoriesView`/`SuppliersView`.
- **Cột thao tác:** mặc định **Xem (mắt E7B3) + Xóa (thùng rác E74D)**. Bảng có nhu cầu sửa nhanh thì chèn thêm **Sửa (bút E70F)** ở giữa (mở view rồi vào thẳng edit) — xem `ReceiptsView`, `WoodSubCategoriesView`.
- **Nút Hủy trong form = xác nhận trước khi bỏ, tùy chế độ.** Đặt tên nút `FormCancelBtn`; ở `add` label "Hủy bỏ", ở `edit` label "Hủy sửa". `BtnCancelAdd_Click`: `add` → dialog "Các thông tin chưa được lưu, tiếp tục huỷ?" (Yes = đóng form); `edit` → dialog "Những thay đổi sẽ không được lưu, tiếp tục huỷ?" (Yes = nạp lại entity gốc từ AppState + `EnterViewMode`, KHÔNG lưu); `view` → đóng thẳng không hỏi. Helper chung `ConfirmDiscard(message)` (title "Xác nhận hủy"). Áp ở cả 4 view có view/edit (Suppliers, WoodCategories, QuotationDetail, Receipts) + WoodSubCategories.
- **Báo giá = 1 danh sách / NCC, KHÔNG còn phiên bản (Version/IsActive bỏ hẳn).** `QuotationsView` chỉ là bảng NCC (1 dòng/NCC: `AppState.QuotationItemCount(id)` + `FindQuotation(id).EffectiveDate`). Bấm "Xem báo giá" **điều hướng sang trang chi tiết trong CÙNG tab** (không expand, không mở tab mới): `QuotationsView` chứa `ScrollViewer ListRoot` + `ContentControl DetailHost`; `OpenDetail(sup)` gắn `new QuotationDetailView(sup, BackToList)` vào DetailHost, ẩn ListRoot. `QuotationDetailView` là bảng mục giá của 1 NCC + search/filter + CRUD đầy đủ (dùng `AddQuotationItem/UpdateQuotationItem/DeleteQuotationItem`, get-or-create quotation theo `SupplierId`). Về danh sách qua `BackToList()`.
- **Breadcrumb drill-down trong 1 tab:** gọi `(Window.GetWindow(this) as MainWindow)?.SetBreadcrumbDetail(tênChiTiết, onBack)` khi vào trang con → hiện thêm cấp `… / Báo Giá Gỗ / Tên NCC` và biến "Báo Giá Gỗ" thành link xanh gọi `onBack`. `SetBreadcrumbDetail(null)` khi quay lại. `ActivateTab` tự reset về null nên khi đổi tab breadcrumb sạch; view nào đang ở trang con thì re-apply trong `RefreshView()`.
- **Validate nhập thiếu = cảnh báo inline dưới field, KHÔNG MessageBox.** Mỗi field bắt buộc có `<TextBlock Style="{StaticResource FieldWarn}">` (đỏ, ẩn sẵn) ngay dưới; TextBox gắn `Tag="{Binding ElementName=WXxx}"` + `TextChanged="Field_Changed"` để tự ẩn cảnh báo khi user gõ. `BtnSave_Click` gọi `ClearWarnings()` rồi `ShowWarn(WXxx, "...")` cho field trống; lỗi nghiệp vụ từ AppState (trùng tên/code) cũng `ShowWarn` dưới field liên quan thay vì dialog. MessageBox chỉ giữ cho lỗi xóa (không có field để gắn).
- **Text hiển thị cần cho copy**: dùng style `CopyableText` (TextBox read-only, không viền, nền trong) thay `TextBlock` — WPF `TextBlock` KHÔNG bôi đen/copy được.

## Bẫy đã dính (đừng dẫm lại)
- **Đổi schema DB (thêm bảng/cột) mà đang dùng `EnsureCreated`:** `EnsureCreated` KHÔNG cập nhật DB đã tồn tại ở `%APPDATA%` → bảng/cột mới thiếu, query crash "no such table/column". Xử lý trong `DbSeeder`: thêm BẢNG mới → raw SQL `CREATE TABLE IF NOT EXISTS` (xem `EnsureWoodCategoriesTable`, `EnsureWoodSubCategoriesTable`); thêm CỘT vào bảng cũ → check `PRAGMA table_info` rồi `ALTER TABLE ADD COLUMN` (helper `AddColumnIfMissing`, hoặc xem `EnsureSupplierColumns`, vì SQLite không có ADD COLUMN IF NOT EXISTS). Seed mới chỉ chạy khi bảng rỗng nên bản ghi cũ sẽ có cột mới = NULL (đúng, không đè dữ liệu). Hoặc bảo dev xóa `%APPDATA%\TimberFlowDesktop` để tạo lại. Project KHÔNG dùng EF Migrations.
- **Cột NOT NULL cũ còn sót sau khi refactor schema → INSERT vỡ ràng buộc.** Vd `QuotationItems` từng có `Thickness REAL NOT NULL` (thời báo giá 1 giá trị), sau đổi sang `ThicknessMin/Max` nhưng cột cũ vẫn còn + vẫn NOT NULL → thêm mục giá mới lỗi `SQLite Error 19: NOT NULL constraint failed`. Fix trong `DbSeeder.EnsureQuotationItemColumns`: backfill xong thì `ALTER TABLE ... DROP COLUMN "Thickness"` (SQLite 3.35+, bundle EF hỗ trợ; bọc try/catch). Khi bỏ 1 cột khỏi model, nhớ DROP luôn cột cũ NOT NULL.
- **`DbUpdateException` nuốt nguyên nhân gốc:** message ngoài chỉ ghi "An error occurred while saving the entity changes, see the inner exception". Muốn thấy lỗi SQLite thật thì gộp chuỗi `InnerException` (helper `Flatten(ex)` trong `QuotationDetailView`) rồi mới show — đừng chỉ `ex.Message`.
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

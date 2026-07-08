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
Domain/     Entities.cs          — POCO thuần (WoodCategory + WoodSubCategory(phân loại con), Supplier{+TaxCode,BankAccount}, WoodLot{+WoodSubType,ThicknessNote,LengthNote,Origin,DeliveryNote}, WoodQuotation, QuotationItem{+WoodSubType}, WarehouseReceipt/Issue, Order...) + enum VolumeRule
            QuotationPriceMatcher — khớp giá 1 kiện với báo giá NCC (specificity kiểu firewall rule; con trước, fallback cha)
            WoodVolumeCalculator — TOÀN BỘ công thức nghiệp vụ nằm ở đây, static, dùng chung
Data/       AppDbContext.cs      — EF Core + SQLite, cấu hình quan hệ/index
            DbSeeder.cs          — seed dữ liệu mẫu, CHỈ seed khi bảng rỗng
            AppState.cs          — kho dữ liệu in-memory dùng chung + mọi nghiệp vụ ghi
Helpers/    Fmt.cs               — format số/tiền/ngày (vi-VN thống nhất toàn app) + ParseNum (parse ngược, vi-VN aware)
            GridLayoutStore.cs   — lưu/khôi phục thứ tự + độ rộng cột DataGrid theo user
            RowNumberConverter.cs — STT theo AlternationIndex, luôn tăng dần bất kể sort/filter
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

**`WoodLot` + `QuotationItem` lưu cả `WoodType` (cha) + `WoodSubType` (con, nullable).** Rule vẫn tra theo `WoodType` (cha) nên `WoodVolumeCalculator` KHÔNG đổi. Khớp giá: `QuotationPriceMatcher.FindBestMatch(..., woodSubType)` **ưu tiên khớp đúng con, không có thì fallback dòng giá cấp cha** (mọi field NULL trên dòng giá = wildcard; con để trống = áp mọi con). Ở **Nhập Kho** phân loại con **BẮT BUỘC nếu cha có con**; ở **Báo Giá** để trống là hợp lệ (chính là giá cấp cha). Báo giá **không còn khai `Grade`** (field `QuotationItem.Grade` vẫn còn trong entity nhưng luôn = NULL) → matcher coi grade là wildcard, chỉ khớp theo Loại/Con/Xuất xứ/kích thước.

**Gỗ nhóm `ByFootage`** — mm chính xác vô nghĩa (công thức chỉ dùng Footage) nên độ dày + độ dài chỉ mô tả:
- Độ dày nhập ký hiệu inch: `1"`, `4/4"`, `5/4"`... — parse bằng `WoodVolumeCalculator.ParseFootageThicknessMm(text)`; giữ ký hiệu gốc ở `WoodLot.ThicknessNote`.
- Độ dài nhập nhiều giá trị inch (vd `96"108"120"`) lưu ở `WoodLot.LengthNote`. Cả hai chỉ hiển thị, không tính m³.

## Quy tắc & pattern PHẢI theo
- **Nghiệp vụ tính toán chỉ có 1 chỗ:** `WoodVolumeCalculator`. Đừng bao giờ nhân/chia công thức thể tích hay giá vốn rải rác trong View. Công thức: rule ByFootage = `(Footage/1000)*2.36`; BySpecification = `Dài*Rộng*Dày*SốLượng/1e9`. Giá vốn VND/m³ = `USD * tỷ giá * (1 + thuế%/100)`.
- **Mọi ghi dữ liệu đi qua `AppState`** (AddCategory/UpdateCategory/DeleteCategory, AddSubCategory/UpdateSubCategory/DeleteSubCategory, AddSupplier/UpdateSupplier/DeleteSupplier, AddReceipt/**UpdateReceipt**/**DeleteReceipt**, AddIssue, AddQuotationItem/UpdateQuotationItem/DeleteQuotationItem/DeleteQuotation, DeleteLot). Kiện gỗ (`WoodLot`) CHỈ được tạo/sửa/xóa qua phiếu Nhập Kho (`AddReceipt`/`UpdateReceipt`/`DeleteReceipt`) — `LotsView` (Quản Lý Kiện Gỗ) thuần xem/lọc/xóa. Cascade & chặn: `UpdateCategory` đổi TÊN cha → cascade `WoodType` mọi WoodLot+QuotationItem; `UpdateSubCategory` đổi tên con → cascade `WoodSubType`; `DeleteSupplier`/`DeleteCategory`/`DeleteSubCategory` chặn nếu đang được tham chiếu; `UpdateReceipt`/`DeleteReceipt` chặn nếu phiếu có kiện **đã phát sinh xuất kho** (giữ nhất quán tồn kho), `UpdateReceipt` xóa hết kiện cũ rồi thêm lại (2 lần `SaveChanges` để tái dùng mã kiện không trùng khóa). Mỗi hàm tự mở `AppDbContext`, `SaveChanges`, rồi `Reload()` + bắn event `Changed`. View đừng tự đụng DbContext để ghi.
- **View nào cần tự làm mới thì implement `IModuleView.RefreshView()`.** MainWindow gọi lại khi đổi tab / khi `AppState.Changed`.
- **UI build bằng code-behind là chủ đạo** (thẻ, badge, form... dựng bằng C# trong `Build...()`), XAML chỉ dựng khung + style. Giữ nguyên phong cách đó cho nhất quán, đừng nửa nạc nửa mỡ.
- **Bảng dữ liệu = `DataGrid` (style `DataTable` trong App.xaml)**, KHÔNG dựng tay bằng Grid/ItemsControl nữa. Cho sẵn kéo đổi thứ tự cột/kéo giãn cột/border dọc — **sort bằng click header đã TẮT** (`CanUserSortColumns="False"` trong style `DataTable`, cố ý, đừng bật lại). Pattern: mỗi View có 1 class `XxxRow` (bọc entity + expose property cho binding), `List<XxxRow> _rows` + `ICollectionView _view = CollectionViewSource.GetDefaultView(_rows)` với `_view.Filter = FilterPredicate`; cột định nghĩa trong XAML (DataGridTextColumn dùng `ElementStyle`; ô đặc biệt/badge/nút thao tác dùng `DataGridTemplateColumn` + `Click` handler lấy `DataContext` ra `XxxRow`). Search/filter = TextBox + ComboBox gọi `_view.Refresh()`. **Đặt tên DataGrid KHÁC "Grid"** (vd `HistoryGrid`, `LotGrid`) vì code-behind còn dùng `Grid.SetColumn(...)` — trùng tên sẽ lỗi biên dịch.
- **Cột STT bắt buộc, đầu tiên, mọi bảng:** `DataGridTextColumn Header="STT" Width="50" MinWidth="50" IsReadOnly CanUserReorder="False" CanUserSort="False"`, `Binding` = `AlternationIndex` (qua `RelativeSource AncestorType=DataGridRow`) + `Converter={StaticResource RowNumberConverter}` (1-based, luôn đúng thứ tự hiển thị dù sort/filter đổi). `DataTable` style có `AlternationCount="100000"` sẵn để nuôi binding này.
- **MỌI cột DataGrid phải có `MinWidth`, KHÔNG dùng `Width="*"`/`"2*"`/Star nữa.** Lý do: WPF DataGrid tự động co cột (kể cả cột pixel cố định) xuống sát `MinWidth` để luôn vừa khung hình thay vì cuộn ngang — Star width làm chuyện này tệ hơn (cột "hút" hết chỗ trống, kéo cột A làm cột B đổi theo). Với các cột "mô tả dài" từng là Star (tên NCC, địa chỉ, ghi chú...) phải set **`MinWidth = Width`** (không có khoảng đệm) để chặn hẳn kiểu tự-co này, ép DataGrid cuộn ngang thật sự khi tràn (`ScrollViewer.HorizontalScrollBarVisibility="Auto"` đã có sẵn trong style `DataTable`). Cột số/ngày/mã có thể để `MinWidth` thấp hơn `Width` một chút (10-30px) để vẫn kéo tay thu hẹp được.
- **Bảng khai kiện gỗ trong form Nhập Kho là NGOẠI LỆ** (không dùng DataGrid, vì cần ô nhập động + tính realtime theo dòng — DataGrid không hợp cho việc này): mỗi dòng kiện là 1 `Grid` dựng tay, 2 biến thể theo chế độ — `BuildLotRow` (add/edit, có ô nhập + nút xóa) và `BuildLotRowReadOnly` (view, thuần `TextBlock`, không sửa/xóa được); `RebuildLotRows()` chọn biến thể theo `_mode`. Cả 2 tự vẽ scrollbar ngang riêng (`ScrollViewer HorizontalScrollBarVisibility="Auto"` bọc ngoài + `StackPanel MinWidth` cố định lớn hơn tổng cột) — đây chính là mẫu tham khảo khi cần bảng cuộn ngang mà không dùng DataGrid. Header (XAML) và row (code-behind) phải KHỚP số cột + độ rộng ở CẢ HAI biến thể. Thứ tự cột hiện tại: STT · Mã kiện · **Phiếu giao hàng** · Loại gỗ(cha) · Phân loại(con) · Xuất xứ · Dày · Rộng · Dài · Footage · Số lượng · Thể tích m³ · Đơn giá USD · Tổng tiền USD · Tổng tiền VND · Tiền thuế VND · Tổng cộng VND · Xóa (**không có** cột Giá vốn VND — đã bỏ). Tỷ giá + % Thuế nhập khẩu khai báo ở **header phiếu** (`FExchangeRate`/`FTaxPercent`, áp dụng chung mọi kiện trong phiếu), không còn theo từng dòng kiện nữa. `ApplyRuleVisibility()`: gỗ **quy cách** ẩn cột Footage; gỗ **footage** ẩn cột Rộng (cột Dài: quy cách = mm dùng tính m³, footage = ký hiệu inch mô tả). `DraftLot` mới để **TRỐNG hết mọi field** (loại gỗ = placeholder "— Chọn loại gỗ —"; validate bắt buộc Mã kiện + Loại gỗ khi lưu). Cột "Xuất xứ" = `WoodLot.Origin` (THAY cho Grade cũ) — matcher khớp giá theo Origin, không còn theo Grade.
- **Đơn giá USD tự tra theo báo giá NCC + cập nhật realtime** khi đổi loại gỗ/phân loại con/xuất xứ/độ dày/rộng/dài (mọi field ảnh hưởng đến `QuotationPriceMatcher.FindBestMatch` đều gọi lại `Recalculate` trong `Update()` của dòng đó). **Chưa khớp được báo giá nào → hiện chữ "Chưa xác định"** (không tô đỏ giá trị cũ) thay cho placeholder rỗng: ở `BuildLotRow` là 1 `TextBlock` "Chưa xác định" đè lên `priceBox` trống (kiểu overlay giống `SearchHint`); ở `BuildLotRowReadOnly`/view mode là text "Chưa xác định" màu `Slate400` thay `$0`. Đổi field làm mất khớp phải RESET lại ô giá về rỗng/màu thường, đừng để giá cũ (đã khớp trước đó) còn sót lại trên UI.
- **Bấm Lưu ở add/edit không đóng form** — sau khi `AddReceipt`/`UpdateReceipt` thành công, gọi `EnterViewMode(saved)` để chuyển thẳng sang xem chi tiết phiếu vừa lưu (không collapse form).
- **Lưu bố cục cột (thứ tự + độ rộng):** gọi `Helpers.GridLayoutStore.Attach(TheGrid, "khóa")` trong constructor sau khi cột đã tạo. Nó tự đọc/ghi `%APPDATA%\TimberFlowDesktop\grid-layout.json` (per-user) → giữ nguyên khi đổi tab hay thoát/mở lại app. Mỗi bảng 1 khóa riêng (categories/suppliers/receipts/issues/lots/quotation-suppliers/quotation-items). Bảng mới nhớ Attach với khóa mới. Khóa cột định danh theo Header nên đừng trùng Header trong cùng bảng. Cột "STT" luôn bị ghim `DisplayIndex = 0` trong `ApplySaved()` (layout cũ lưu từ trước khi có cột STT không được đẩy nó ra sau). `Attach()` còn tự "mồi" lại `Width` mọi cột ngay sau khi `Loaded` (`PrimeColumnWidths`, đổi giá trị rồi đổi lại) — xem bẫy "kéo giãn cột lần đầu không ăn" bên dưới, đừng xóa đoạn này.
- **Style lấy từ App.xaml** qua `FindResource(...)` / `{StaticResource ...}`. Thêm màu/brush mới thì khai báo trong App.xaml, đừng hard-code hex trong View trừ khi thật sự cần alpha đặc biệt.
- **Thanh cuộn (ScrollBar) mảnh, theo theme slate** — style global `TargetType="ScrollBar"` trong App.xaml (không cần `{StaticResource}`, tự áp cho mọi ScrollViewer/DataGrid trong app), 6px, bo tròn viên nang, đậm dần khi hover/kéo. Có `RepeatButton` trong suốt cho vùng track để bấm-cuộn-từng-trang vẫn hoạt động (template gốc không có, phải tự thêm). Đừng đụng vào `ScrollViewer.CanContentScroll` để "sửa" hành vi DataGrid — không liên quan, xem bẫy DataGrid tự co cột bên dưới.
- **Form add/edit giãn FULL chiều ngang, chia đều** — KHÔNG đặt `MaxWidth`/`HorizontalAlignment="Left"` trên `AddFormPanel`. Các field xếp trên Grid cột `*` để tự chia đều theo bề rộng. Bấm Add đẩy bảng detail xuống (form nằm trên bảng trong cùng StackPanel).
- **Xem chi tiết = dùng lại form add/edit ở chế độ read-only** (không mở dialog riêng). 3 chế độ dùng field `_mode` = `add|view|edit`: `EnterViewMode` điền dữ liệu + `SetReadOnly(true)` + đổi nút chính thành "Chỉnh sửa"; bấm nút đó gọi `EnterEditMode` (mở khóa + nút "Cập nhật"); `BtnSave_Click` đầu hàm `if (_mode=="view") { EnterEditMode(); return; }`. Nút "Thêm mới" khi đang xem/sửa thì chuyển thẳng sang add mode (clear), chỉ đóng form khi đang ở add mode. Xem `WoodCategoriesView`/`SuppliersView`.
- **Cột thao tác:** mặc định **Xem (mắt E7B3) + Xóa (thùng rác E74D)**. Bảng có nhu cầu sửa nhanh thì chèn thêm **Sửa (bút E70F)** ở giữa (mở view rồi vào thẳng edit) — xem `ReceiptsView`, `WoodSubCategoriesView`.
- **Nút Hủy trong form = xác nhận trước khi bỏ, tùy chế độ.** Đặt tên nút `FormCancelBtn`; ở `add` label "Hủy bỏ", ở `edit` label "Hủy sửa". `BtnCancelAdd_Click`: `add` → dialog "Các thông tin chưa được lưu, tiếp tục huỷ?" (Yes = đóng form); `edit` → dialog "Những thay đổi sẽ không được lưu, tiếp tục huỷ?" (Yes = nạp lại entity gốc từ AppState + `EnterViewMode`, KHÔNG lưu); `view` → đóng thẳng không hỏi. Helper chung `ConfirmDiscard(message)` (title "Xác nhận hủy"). Áp ở cả 4 view có view/edit (Suppliers, WoodCategories, QuotationDetail, Receipts) + WoodSubCategories.
- **Báo giá = 1 danh sách / NCC, KHÔNG còn phiên bản (Version/IsActive bỏ hẳn).** `QuotationsView` chỉ là bảng NCC (1 dòng/NCC: `AppState.QuotationItemCount(id)` + `FindQuotation(id).EffectiveDate`). Bấm "Xem báo giá" **điều hướng sang trang chi tiết trong CÙNG tab** (không expand, không mở tab mới): `QuotationsView` chứa `ScrollViewer ListRoot` + `ContentControl DetailHost`; `OpenDetail(sup)` gắn `new QuotationDetailView(sup, BackToList)` vào DetailHost, ẩn ListRoot. `QuotationDetailView` là bảng mục giá của 1 NCC + search/filter + CRUD đầy đủ (dùng `AddQuotationItem/UpdateQuotationItem/DeleteQuotationItem`, get-or-create quotation theo `SupplierId`). Về danh sách qua `BackToList()`. Form hàng đầu: **Loại gỗ · Phân loại con · Xuất xứ** (KHÔNG còn field/cột "Chất lượng" — đã bỏ, phân loại con dời vào đúng chỗ Chất lượng cũ); `BtnSave_Click` set `Grade = null` cứng. Cột bảng: STT · Loại gỗ(hiện "Gỗ X · Con") · Độ dày · Rộng · Dài · Xuất xứ · Ghi chú · Đơn giá USD · **Chỉnh sửa lần cuối** (`UpdatedAt`, format `yyyy/MM/dd HH:mm:ss`, giá trị ban đầu = lúc tạo) · Thao tác (tách ActionGrid bên phải, cùng `_view`).
- **Breadcrumb drill-down trong 1 tab:** gọi `(Window.GetWindow(this) as MainWindow)?.SetBreadcrumbDetail(tênChiTiết, onBack)` khi vào trang con → hiện thêm cấp `… / Báo Giá Gỗ / Tên NCC` và biến "Báo Giá Gỗ" thành link xanh gọi `onBack`. `SetBreadcrumbDetail(null)` khi quay lại. `ActivateTab` tự reset về null nên khi đổi tab breadcrumb sạch; view nào đang ở trang con thì re-apply trong `RefreshView()`.
- **Validate nhập thiếu = cảnh báo inline dưới field, KHÔNG MessageBox.** Mỗi field bắt buộc có `<TextBlock Style="{StaticResource FieldWarn}">` (đỏ, ẩn sẵn) ngay dưới; TextBox gắn `Tag="{Binding ElementName=WXxx}"` + `TextChanged="Field_Changed"` để tự ẩn cảnh báo khi user gõ. `BtnSave_Click` gọi `ClearWarnings()` rồi `ShowWarn(WXxx, "...")` cho field trống; lỗi nghiệp vụ từ AppState (trùng tên/code) cũng `ShowWarn` dưới field liên quan thay vì dialog. MessageBox chỉ giữ cho lỗi xóa (không có field để gắn).
- **Text hiển thị cần cho copy**: dùng style `CopyableText` (TextBox read-only, không viền, nền trong) thay `TextBlock` — WPF `TextBlock` KHÔNG bôi đen/copy được.
- **Field ngày tháng = `DatePicker`** (style `DatePickerField` trong App.xaml, ControlTemplate riêng bo góc 8px khớp theme), KHÔNG dùng `TextBox` gõ tay ngày nữa.
- **Format số thống nhất `vi-VN` toàn app** (dấu chấm = hàng nghìn, dấu phẩy = thập phân — vd `392.286.085 ₫`, `2.400,50`), áp dụng cho MỌI hiển thị số (m³, USD, VND) qua `Fmt`. Khi cần parse ngược text người dùng gõ về số, LUÔN dùng `Fmt.ParseNum()` (vi-VN aware) — đừng dùng `double.Parse`/`CultureInfo.InvariantCulture` trực tiếp, sẽ đọc sai giá trị ≥1000 (vd "2.400" bị hiểu thành 2.4).

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
- **DataGrid tự co cột thay vì cuộn ngang:** dù cột đã set `Width` pixel cố định + `MinWidth`, WPF DataGrid vẫn tự bóp từng cột xuống sát `MinWidth` để luôn vừa khung hình, CHỈ khi tất cả cột đã chạm sàn `MinWidth` mà vẫn tràn thì mới bắt đầu cuộn ngang (`ScrollViewer.CanContentScroll` True/False không ảnh hưởng chuyện này, đã thử). Muốn 1 cột KHÔNG BAO GIỜ bị tự-co (ép cuộn ngang thay vì bóp) thì set `MinWidth = Width` cho đúng cột đó — xem toàn bộ cột "mô tả dài" trong Suppliers/WoodCategories/Quotations/... đã áp dụng.
- **Kéo giãn viền cột DataGrid lần đầu cài đặt không ăn** (phải double-click viền 1 lần thì các lần kéo sau mới bình thường): do `GridLayoutStore.ApplySaved()` chỉ thật sự set lại `Width` từng cột khi ĐÃ có file `grid-layout.json` từ trước — việc set Width đó vô tình "mồi" đúng phần hình học của gripper resize; lần cài đặt đầu tiên chưa có file nên bước này không chạy. Fix: `Attach()` tự gọi `PrimeColumnWidths()` (đổi Width mỗi cột sang giá trị khác rồi đổi lại) ngay sau `grid.Loaded`, mô phỏng đúng thao tác double-click đó cho MỌI lần mở app. Đừng xóa đoạn `PrimeColumnWidths`/`grid.Loaded` này trong `GridLayoutStore.cs`.
- **Format số:** đã thống nhất `vi-VN` cho MỌI số trong app (xem mục Format số ở trên) — đừng quay lại `InvariantCulture` hay tự chế "không phân cách hàng nghìn" nữa, code cũ từng làm vậy và gây lỗi round-trip parse.

## Khi kiểm chứng thay đổi UI
Build → chạy exe → chụp màn hình → đọc lại ảnh để xác nhận, đừng chỉ tin "build succeeded". Trước giờ tao toàn làm vậy và bắt được lỗi (icon mất, chart lệch...).

## Đừng làm
- Đừng seed đè dữ liệu người dùng (DbSeeder chỉ chạy khi rỗng — giữ nguyên).
- Đừng commit / push nếu mày không bảo — dù giờ đã là git repo có remote GitHub (`vphatt/Wood-Inventory`) rồi, vẫn phải đợi lệnh mới commit/push.
- Đừng thêm lại thứ gì thuộc bản web.

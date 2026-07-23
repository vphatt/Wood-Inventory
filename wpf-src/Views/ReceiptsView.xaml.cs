using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WoodInventory.Data;
using WoodInventory.Domain;
using WoodInventory.Helpers;

namespace WoodInventory.Views;

public partial class ReceiptsView : UserControl, IModuleView
{
    /// <summary>Một dòng kiện gỗ đang khai báo trong phiếu nhập.</summary>
    private sealed class DraftLot
    {
        // Dòng kiện mới: mọi field để TRỐNG (loại gỗ = placeholder chưa chọn).
        public string Id = "";
        public string DeliveryNote = "";
        public string WoodType = "";
        public string WoodSubType;   // phân loại con (null = chưa chọn/không phân loại)
        public string Thickness = "";
        public string Origin = "";   // Xuất xứ
        public string Grade = "";    // Chất lượng (Grade) — khớp báo giá như Xuất xứ
        public string Width = "";
        public string Length = "";
        public string LengthNote;    // độ dài dạng inch cho gỗ footage, vd 96"108"120"
        public string Footage = "";
        public string Quantity = "";
        public string ManualPriceText = "";
        public int VolumeDecimals = AppState.Settings.DefaultVolumeDecimals;   // số chữ số thập phân làm tròn m³ riêng dòng này
        public double VolumeAdjustment;    // điều chỉnh tay +/- cộng vào m³ sau khi làm tròn (mặc định 0)

        // Kết quả tính toán gần nhất
        public double Cbm;
        public decimal EffectivePrice;   // LUÔN là con số đang có trong ô Đơn giá (user sửa đè được)
        public decimal CostPriceVnd;
        public decimal TotalPrice;
        public decimal TotalVnd;
        public decimal TaxVnd;
        public decimal TotalValueVnd;
        public bool PriceFromQuotation;  // có khớp được 1 dòng báo giá không

        // Dòng báo giá khớp được (để so lệch + tooltip + cập nhật ngược khi lưu)
        public decimal QuotedPrice;      // đơn giá trên báo giá (0 = không khớp dòng nào)
        public string QuotedCurrency;    // đơn vị của dòng báo giá đó (tooltip hiện đúng $ hay ₫)
        public string QuotedItemId;      // Id dòng báo giá — dùng khi user chọn "cập nhật đơn giá mới cho báo giá"
        public decimal LastQuoted;       // giá báo giá lần tự-điền gần nhất → biết match có ĐỔI để điền lại ô
    }

    private readonly List<DraftLot> _draftLots = new();

    private string _mode = "add";              // add | view | edit
    private string _editingReceiptId;          // id phiếu đang xem/sửa
    private bool ReadOnly => _mode == "view";

    private ReceiptReportView _reportView;     // != null khi đang ở trang Bảng tổng chi tiết nhập kho
    private MainWindow Main => Window.GetWindow(this) as MainWindow;

    public ReceiptsView()
    {
        InitializeComponent();
        FDate.SelectedDate = DateTime.Today;
        FCurrency.SelectedIndex = 0;
        InitTaxCombo();
        ResetDraft();
        RefreshView();
        Helpers.GridLayoutStore.Attach(HistoryGrid, "receipts");
        Helpers.GridPairSync.Link(HistoryGrid, ActionGrid);
    }

    private void InitTaxCombo()
    {
        foreach (var t in new[] { "0", "5", "8", "10" })
            FTaxPercent.Items.Add(new ComboBoxItem { Content = $"{t}%", Tag = t });
        SelectTaxPercent(AppState.Settings.DefaultTaxPercent);
    }

    /// <summary>Chọn item khớp % thuế đã cho trong FTaxPercent; không khớp giá trị nào thì rơi về 10%.</summary>
    private void SelectTaxPercent(decimal percent)
    {
        var tag = Fmt.Num((double)percent);
        FTaxPercent.SelectedIndex = -1;
        foreach (ComboBoxItem it in FTaxPercent.Items)
            if ((it.Tag as string) == tag) { FTaxPercent.SelectedItem = it; break; }
        if (FTaxPercent.SelectedIndex < 0) FTaxPercent.SelectedIndex = 3;
    }

    /// <summary>Đơn vị tiền tệ + Tỷ giá + thuế nhập khẩu áp dụng chung cho cả phiếu — không khai báo riêng theo từng kiện.</summary>
    private double SelectedExchangeRate => D(FExchangeRate.Text);
    private double SelectedTaxPercent => D((FTaxPercent.SelectedItem as ComboBoxItem)?.Tag as string ?? "10");
    private string SelectedCurrency => (FCurrency.SelectedItem as ComboBoxItem)?.Tag as string ?? "USD";

    /// <summary>
    /// Chọn VND → khóa Tỷ giá về 1 (đơn giá kiện là VND rồi, nhân 1 là no-op nhưng công thức chung
    /// vẫn nhân Tỷ giá như cũ, không cần nhánh riêng theo đơn vị). Chọn lại USD → mở khóa, trả về Tỷ giá mặc định.
    /// </summary>
    private void FCurrency_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyCurrencyLock();
        FExchangeRate.Text = SelectedCurrency == "VND" ? "1" : Fmt.Num((double)AppState.Settings.DefaultExchangeRate);
        if (!IsLoaded) return;
        foreach (var update in _rowUpdaters.ToList()) update();
    }

    /// <summary>
    /// Khoá cặp Đơn vị tiền tệ / Tỷ giá: chế độ XEM khoá cả hai (chỉ đọc, không thao tác được);
    /// còn lại chỉ khoá Tỷ giá khi đơn vị là VND (tỷ giá luôn = 1). Gọi lại mỗi khi đổi chế độ hoặc đổi đơn vị.
    /// </summary>
    private void ApplyCurrencyLock()
    {
        var gray = (Brush)FindResource("Slate50");
        FCurrency.IsEnabled = !ReadOnly;
        FCurrency.Background = ReadOnly ? gray : Brushes.White;   // style "Select" không có trigger IsEnabled
        var rateLocked = ReadOnly || SelectedCurrency == "VND";
        FExchangeRate.IsReadOnly = rateLocked;
        FExchangeRate.Background = rateLocked ? gray : Brushes.White;
    }

    private void ResetDraft()
    {
        _draftLots.Clear();
        _draftLots.Add(new DraftLot());
        RebuildLotRows();
    }

    public void RefreshView()
    {
        // Đang ở trang Bảng tổng chi tiết nhập kho → cập nhật nó + giữ breadcrumb
        if (_reportView != null)
        {
            _reportView.RefreshView();
            Main?.SetBreadcrumbDetail(Lang.T("Receipts.OpenReportButton"), BackToReceipts);
            return;
        }

        var current = (FSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        FSupplier.Items.Clear();
        FSupplier.Items.Add(new ComboBoxItem { Content = Lang.T("Receipts.SupplierPlaceholder"), Tag = "" });
        foreach (var s in AppState.Suppliers)
            FSupplier.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id });
        foreach (ComboBoxItem item in FSupplier.Items)
            if ((item.Tag as string) == current) FSupplier.SelectedItem = item;
        if (FSupplier.SelectedItem == null) FSupplier.SelectedIndex = 0;

        RebuildHistory();

        // Tính lại giá/thành tiền các dòng kiện đang khai báo theo báo giá NCC mới nhất (vd người dùng vừa
        // sửa báo giá ở tab khác) — KHÔNG đụng vào dữ liệu đã gõ. Đang add/edit: chỉ update giá trị hiển thị
        // qua _rowUpdaters (giữ nguyên control, không mất focus). Đang view: rows là TextBlock tĩnh nên rebuild
        // thẳng cho đơn giản (không có gì để mất).
        if (_mode == "view") RebuildLotRows();
        else
        {
            // Báo giá NCC có thể vừa đổi ở tab khác → nạp lại dropdown loại gỗ/phân loại con trước khi tính lại giá.
            foreach (var rebuild in _rowSupplierRebuilders.ToList()) rebuild();
            foreach (var update in _rowUpdaters.ToList()) update();
        }
    }

    private static double D(string s) => Fmt.ParseNum(s);

    private static string Blank2Null(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Parse độ dày theo loại gỗ — nhóm Footage chấp nhận thêm ký hiệu inch/quarter (1", 4/4").</summary>
    private static double ParseThickness(DraftLot lot) =>
        AppState.GetVolumeRule(lot.WoodType) == VolumeRule.ByFootage
            ? WoodVolumeCalculator.ParseFootageThicknessMm(lot.Thickness)
            : D(lot.Thickness);

    private string SelectedSupplierId => (FSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>Tra dòng giá từ báo giá của NCC — khớp dòng giá cụ thể nhất theo loại gỗ + kích thước + grade.</summary>
    private static QuotationItem LookupQuotationItem(string supplierId, string woodType, string woodSubType,
        double thickness, double width, double length, string origin, string grade)
    {
        if (string.IsNullOrEmpty(supplierId)) return null;
        var quotation = AppState.Quotations.FirstOrDefault(q => q.SupplierId == supplierId);
        if (quotation == null) return null;
        return QuotationPriceMatcher.FindBestMatch(quotation.Items, woodType,
            thickness: thickness, width: width, length: length, origin: origin, grade: grade, woodSubType: woodSubType);
    }

    private void Recalculate(DraftLot lot)
    {
        var thickness = ParseThickness(lot);
        var matched = LookupQuotationItem(SelectedSupplierId, lot.WoodType, lot.WoodSubType, thickness, D(lot.Width), D(lot.Length), lot.Origin, lot.Grade);
        lot.PriceFromQuotation = matched != null && matched.Price > 0;
        lot.QuotedPrice = lot.PriceFromQuotation ? matched.Price : 0;
        lot.QuotedCurrency = lot.PriceFromQuotation ? matched.PriceCurrency : null;
        lot.QuotedItemId = lot.PriceFromQuotation ? matched.Id : null;
        // Đơn giản hóa: không quan tâm báo giá NCC ghi theo USD hay VND, chỉ lấy con số — đơn vị tiền tệ
        // do người dùng chọn ở header phiếu (FCurrency) quyết định, Tỷ giá luôn nhân như cũ (VND thì Tỷ giá đã bị khóa = 1).
        // Đơn giá LUÔN lấy con số đang có trong ô: giá báo giá chỉ được TỰ ĐIỀN vào ô (xem Update() trong BuildLotRow),
        // sau đó user có quyền sửa đè — lệch so với báo giá thì hiện icon ≠ và cảnh báo lúc lưu.
        lot.EffectivePrice = (decimal)D(lot.ManualPriceText);
        lot.Cbm = WoodVolumeCalculator.CalculateVolume(AppState.GetVolumeRule(lot.WoodType), thickness, D(lot.Width),
            D(lot.Length), (int)D(lot.Quantity), D(lot.Footage), lot.VolumeDecimals, lot.VolumeAdjustment);
        lot.CostPriceVnd = WoodVolumeCalculator.CalculateCostPricePerM3(lot.EffectivePrice,
            (decimal)SelectedExchangeRate, (decimal)SelectedTaxPercent);
        lot.TotalPrice = WoodVolumeCalculator.CalculateTotalPrice(lot.EffectivePrice, lot.Cbm);
        lot.TotalVnd = WoodVolumeCalculator.ConvertToVnd(lot.TotalPrice, (decimal)SelectedExchangeRate);
        lot.TaxVnd = WoodVolumeCalculator.CalculateTaxAmountVnd(lot.TotalVnd, (decimal)SelectedTaxPercent);
        lot.TotalValueVnd = lot.TotalVnd + lot.TaxVnd;
    }

    // ---------------- Bảng khai báo ----------------

    private void RebuildLotRows()
    {
        LotRowsPanel.Items.Clear();
        _rowUpdaters.Clear();
        _rowSupplierRebuilders.Clear();
        _rowNavs.Clear();
        _rowHighlighters.Clear();
        if (_selectedLot != null && !_draftLots.Contains(_selectedLot)) _selectedLot = null;   // dòng đã bị xóa
        for (var i = 0; i < _draftLots.Count; i++)
        {
            var lot = _draftLots[i];
            LotRowsPanel.Items.Add(_mode == "view" ? BuildLotRowReadOnly(lot, i + 1) : BuildLotRow(lot, i + 1));
        }
        UpdateTotals();
        ApplyLotScrollLimit();
    }

    /// <summary>Giới hạn chiều cao vùng cuộn danh sách kiện: xem = 16 dòng, thêm/sửa = 10 dòng (đo chiều cao dòng
    /// thực tế sau khi layout để ra đúng số dòng bất kể DPI/cỡ chữ; nhiều hơn thì cuộn dọc nội bộ).</summary>
    private void ApplyLotScrollLimit()
    {
        var limit = _mode == "view" ? 16 : 10;
        LotBodyScroll.Dispatcher.BeginInvoke(new Action(() =>
        {
            double perRow = 0;
            if (LotRowsPanel.Items.Count > 0
                && LotRowsPanel.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement fe && fe.ActualHeight > 0)
                perRow = fe.ActualHeight;
            if (perRow <= 0) perRow = _mode == "view" ? 29 : 45;   // fallback nếu chưa realize
            LotBodyScroll.MaxHeight = perRow * limit + 12;          // +12 chừa thanh cuộn ngang dưới đáy body
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Body cuộn ngang -> kéo header cuộn ngang theo (căn cột khớp). Header không có thanh cuộn riêng nên không dội ngược.
    private void LotBodyScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0)
            LotHeaderScroll.ScrollToHorizontalOffset(LotBodyScroll.HorizontalOffset);
    }

    /// <summary>Chế độ xem: danh sách kiện hiển thị thuần bảng đọc (TextBlock), không phải field form.</summary>
    private FrameworkElement BuildLotRowReadOnly(DraftLot lot, int stt)
    {
        Recalculate(lot);
        var isFootage = AppState.GetVolumeRule(lot.WoodType) == VolumeRule.ByFootage;

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        // Cột 6 = Chất lượng (Grade), chèn giữa Xuất xứ (5) và Dày (7) → các cột sau dịch +1.
        foreach (var w in new[] { 45.0, 100, 120, 120, 120, 95, 95, 95, 95, 95, 95, 70, 90, 70, 100, 140, 110, 120, 110, -1, 50 })
            grid.ColumnDefinitions.Add(w < 0
                ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 130 }
                : new ColumnDefinition { Width = new GridLength(w) });

        void Cell(string text, int col, HorizontalAlignment align, bool mono = true,
            Brush color = null, FontWeight? weight = null, Thickness? margin = null)
        {
            var tb = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(text) ? "-" : text,
                Foreground = color ?? (Brush)FindResource("Slate700"),
                HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
                Margin = margin ?? new Thickness(6, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (mono) tb.FontFamily = (FontFamily)FindResource("FontMono");
            if (weight.HasValue) tb.FontWeight = weight.Value;
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        Cell(stt.ToString(), 0, HorizontalAlignment.Center, color: (Brush)FindResource("Slate400"));
        Cell(lot.Id, 1, HorizontalAlignment.Left, weight: FontWeights.SemiBold,
            color: (Brush)FindResource("Slate900"), margin: new Thickness(12, 0, 6, 0));
        Cell(lot.DeliveryNote, 2, HorizontalAlignment.Left, mono: false);
        Cell(lot.WoodType, 3, HorizontalAlignment.Left, mono: false);
        Cell(lot.WoodSubType, 4, HorizontalAlignment.Left, mono: false);
        Cell(lot.Origin, 5, HorizontalAlignment.Center);
        Cell(lot.Grade, 6, HorizontalAlignment.Center);
        Cell(lot.Thickness, 7, HorizontalAlignment.Center);
        Cell(lot.Width, 8, HorizontalAlignment.Center);   // Rộng hiện ở mọi loại (footage nhập được, để trống → "-")
        Cell(isFootage ? lot.LengthNote : lot.Length, 9, HorizontalAlignment.Center);
        Cell(isFootage ? lot.Footage : "-", 10, HorizontalAlignment.Center);
        Cell(lot.Quantity, 11, HorizontalAlignment.Center);
        Cell(Fmt.M3(lot.Cbm, lot.VolumeDecimals), 12, HorizontalAlignment.Right);
        Cell(lot.VolumeDecimals.ToString(), 13, HorizontalAlignment.Center, color: (Brush)FindResource("Slate400"));
        Cell(lot.VolumeAdjustment == 0 ? "-" : (lot.VolumeAdjustment > 0 ? "+" : "") + Fmt.M3(lot.VolumeAdjustment, lot.VolumeDecimals),
            14, HorizontalAlignment.Right, color: (Brush)FindResource(lot.VolumeAdjustment == 0 ? "Slate400" : "Amber600"));
        Cell(lot.EffectivePrice > 0 ? Fmt.Money(lot.EffectivePrice, SelectedCurrency) : Lang.T("Receipts.NoPriceMatch"), 15, HorizontalAlignment.Right,
            color: (Brush)FindResource(lot.EffectivePrice > 0 ? "Slate800" : "Slate400"));
        Cell(Fmt.Money(lot.TotalPrice, SelectedCurrency), 16, HorizontalAlignment.Right);
        Cell(Fmt.Vnd(lot.TotalVnd), 17, HorizontalAlignment.Right);
        Cell(Fmt.Vnd(lot.TaxVnd), 18, HorizontalAlignment.Right);
        Cell(Fmt.Vnd(lot.TotalValueVnd), 19, HorizontalAlignment.Right,
            weight: FontWeights.SemiBold, color: (Brush)FindResource("Emerald600"), margin: new Thickness(6, 0, 12, 0));
        // Cột 20 (Xóa): để trống — chế độ xem không cho xóa.

        var row = new Border
        {
            BorderBrush = (Brush)FindResource("Slate100"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.White,
            Child = grid
        };
        WireRowHighlight(row, lot);
        return row;
    }

    private FrameworkElement BuildLotRow(DraftLot lot, int stt)
    {
        Recalculate(lot);

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        // STT, MãKiện, PhiếuGiaoHàng, LoạiGỗ, PhânLoại, XuấtXứ, ChấtLượng, Dày, Rộng, Dài, Footage, SốLượng, ThểTích, SốTP,
        // ĐiềuChỉnh, ĐơnGiáUSD, TổngTiềnUSD, TổngTiềnVND, TiềnThuếVND, TổngCộngVND(*), Xóa — ChấtLượng(6) chèn giữa XuấtXứ(5) và Dày(7).
        foreach (var w in new[] { 45.0, 100, 120, 120, 120, 95, 95, 95, 95, 95, 95, 70, 90, 70, 100, 140, 110, 120, 110, -1, 50 })
            grid.ColumnDefinitions.Add(w < 0
                ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 130 }
                : new ColumnDefinition { Width = new GridLength(w) });

        // Kết quả (tạo trước để các handler cập nhật được)
        var cbmText = new TextBlock
        {
            Text = Fmt.M3(lot.Cbm, lot.VolumeDecimals), FontFamily = (FontFamily)FindResource("FontMono"),
            FontWeight = FontWeights.Medium, Foreground = (Brush)FindResource("Slate600"),
            Margin = new Thickness(6, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        TextBlock MoneyText(bool bold = false, Brush color = null) => new()
        {
            FontFamily = (FontFamily)FindResource("FontMono"),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = color ?? (Brush)FindResource("Slate800"),
            Margin = new Thickness(6, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        var totalUsdText = MoneyText();
        var totalVndText = MoneyText();
        var taxVndText = MoneyText();
        var grandTotalText = MoneyText(bold: true, color: (Brush)FindResource("Emerald600"));
        grandTotalText.Margin = new Thickness(6, 0, 12, 0);
        totalUsdText.Text = Fmt.Money(lot.TotalPrice, SelectedCurrency);
        totalVndText.Text = Fmt.Vnd(lot.TotalVnd);
        taxVndText.Text = Fmt.Vnd(lot.TaxVnd);
        grandTotalText.Text = Fmt.Vnd(lot.TotalValueVnd);
        // Đơn giá LUÔN sửa được (kể cả khi đã khớp báo giá) — chỉ khóa ở chế độ xem.
        var priceBox = new TextBox
        {
            Style = (Style)FindResource("CellInputMono"),
            Text = lot.ManualPriceText,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            IsReadOnly = ReadOnly
        };
        // Chưa khớp báo giá và cũng chưa nhập tay → hiện "Chưa xác định" đè lên ô trống
        // (giống pattern SearchHint), thay vì chỉ tô đỏ giá trị hiện có.
        var priceHint = new TextBlock
        {
            Text = Lang.T("Receipts.NoPriceMatch"), FontStyle = FontStyles.Italic,
            FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 11,
            Foreground = (Brush)FindResource("Slate400"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 12, 0), IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        // Icon "≠" mép TRÁI ô đơn giá: chỉ hiện khi giá đang nhập LỆCH so với dòng báo giá khớp được.
        // Hover hiện đơn giá gốc trên báo giá (theo đúng đơn vị của dòng báo giá đó).
        var mismatchIcon = new TextBlock
        {
            Text = "≠", FontWeight = FontWeights.Bold, FontSize = 14,
            Foreground = (Brush)FindResource("Amber600"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed
        };

        void Update()
        {
            Recalculate(lot);

            // Báo giá khớp được vừa ĐỔI (đổi loại gỗ/kích thước/xuất xứ...) → tự điền giá mới vào ô,
            // NHƯNG chỉ khi user chưa sửa đè (ô trống, hoặc đang giữ đúng giá báo giá cũ).
            if (lot.QuotedPrice != lot.LastQuoted)
            {
                var untouched = string.IsNullOrWhiteSpace(priceBox.Text)
                    || (lot.LastQuoted > 0 && priceBox.Text == Fmt.Num((double)lot.LastQuoted));
                lot.LastQuoted = lot.QuotedPrice;
                if (untouched)
                {
                    // Khớp mới → tự điền giá; MẤT khớp (QuotedPrice=0) → XÓA ô để hiện lại "Chưa xác định",
                    // KHÔNG giữ giá auto cũ tô đen. (Chỉ giữ nếu user đã sửa tay giá khác → untouched=false.)
                    var auto = lot.QuotedPrice > 0 ? Fmt.Num((double)lot.QuotedPrice) : "";
                    if (priceBox.Text != auto) { priceBox.Text = auto; return; }   // TextChanged sẽ gọi lại Update()
                }
            }

            cbmText.Text = Fmt.M3(lot.Cbm, lot.VolumeDecimals);
            totalUsdText.Text = Fmt.Money(lot.TotalPrice, SelectedCurrency);
            totalVndText.Text = Fmt.Vnd(lot.TotalVnd);
            taxVndText.Text = Fmt.Vnd(lot.TaxVnd);
            grandTotalText.Text = Fmt.Vnd(lot.TotalValueVnd);

            var mismatch = lot.QuotedPrice > 0 && lot.EffectivePrice != lot.QuotedPrice;
            priceBox.Foreground = (Brush)FindResource(
                mismatch ? "Amber600" : lot.QuotedPrice > 0 ? "Emerald600" : "Slate700");
            priceBox.FontWeight = lot.QuotedPrice > 0 ? FontWeights.SemiBold : FontWeights.Normal;
            priceBox.IsReadOnly = ReadOnly;

            mismatchIcon.Visibility = mismatch ? Visibility.Visible : Visibility.Collapsed;
            if (mismatch)
                mismatchIcon.ToolTip = Lang.T("Receipts.PriceMismatch.Tooltip",
                    Fmt.Money(lot.QuotedPrice, lot.QuotedCurrency ?? SelectedCurrency));

            priceHint.Visibility = lot.QuotedPrice == 0 && string.IsNullOrWhiteSpace(lot.ManualPriceText)
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateTotals();
        }
        _rowUpdaters.Add(Update);

        priceBox.TextChanged += (_, _) => { lot.ManualPriceText = priceBox.Text; Update(); };

        // Nhấp đúp icon "≠" → bỏ phần sửa tay, trả ô đơn giá về đúng giá trên báo giá
        // (TextChanged sẽ gọi Update() → icon tự ẩn, ô đổi lại màu khớp báo giá).
        mismatchIcon.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount < 2 || ReadOnly || lot.QuotedPrice <= 0) return;
            priceBox.Text = Fmt.Num((double)lot.QuotedPrice);
            priceBox.CaretIndex = priceBox.Text.Length;
            e.Handled = true;
        };

        // STT (không sửa được, chỉ hiển thị thứ tự)
        var sttText = new TextBlock
        {
            Text = stt.ToString(), Foreground = (Brush)FindResource("Slate400"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(sttText, 0); grid.Children.Add(sttText);

        // 1. Mã kiện
        var idBox = Cell(lot.Id, s => { lot.Id = s; }, mono: true, bold: true);
        idBox.Margin = new Thickness(12, 0, 6, 0);
        Grid.SetColumn(idBox, 1); grid.Children.Add(idBox);

        // 2. Phiếu giao hàng
        var deliveryNoteBox = Cell(lot.DeliveryNote, s => { lot.DeliveryNote = s; }, mono: false);
        deliveryNoteBox.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(deliveryNoteBox, 2); grid.Children.Add(deliveryNoteBox);

        // Gợi ý (tối đa 10) lấy từ báo giá NCC khớp loại gỗ cha + phân loại con của kiện; mỗi field còn LỌC CHÉO
        // theo mọi field KHÁC đã nhập (Xuất xứ/Grade/Dày/Rộng/Dài) bất kể thứ tự nhập — xem ItemsExcept. Dòng giá có khai
        // danh sách giá trị rời rạc (vd "1220/2440/3000") thì TÁCH ra thành từng giá trị riêng để gợi ý.
        // Dòng giá là KHOẢNG/ĐOẠN thì hiện hint ký hiệu ĐẦY ĐỦ dải hợp lệ: "20≤x≤25" (đoạn) / "20<x<25" (khoảng)
        // / "x≥20" / "x≤25" ... — cho user biết chính xác được nhập từ bao nhiêu đến bao nhiêu. Insert nguyên
        // chuỗi vào ô cũng được, user tự sửa thành 1 con số hợp lệ.
        bool Footage() => AppState.GetVolumeRule(lot.WoodType) == VolumeRule.ByFootage;
        List<string> SuggestFrom(IEnumerable<QuotationItem> items, Func<QuotationItem, IEnumerable<string>> pick) =>
            DistinctSuggest(items.SelectMany(pick));
        // Lọc CHÉO: gợi ý của MỘT field chỉ lấy từ dòng giá khớp TẤT CẢ field KHÁC đã nhập (bất kể thứ tự nhập —
        // nhập Dày + Dài trước thì gợi ý Rộng vẫn lọc theo cả hai). Field để trống → không lọc theo field đó.
        // Base MatchingQuotationItems đã lọc sẵn Loại gỗ (cha) + Phân loại con; ở đây lọc thêm Xuất xứ/Grade/kích thước.
        // Text: dòng giá để trống (NULL) = áp mọi giá trị nên vẫn khớp.
        double? Num(string t) => string.IsNullOrWhiteSpace(t) ? (double?)null : D(t);
        bool TxtMatch(string rule, string actual) => string.IsNullOrWhiteSpace(actual)
            || string.IsNullOrWhiteSpace(rule) || string.Equals(rule.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
        bool DimMatch(string vals, double? mn, double? mx, bool op, string entered) =>
            Num(entered) is not double v || QuotationPriceMatcher.DimensionMatches(vals, mn, mx, op, v);
        bool OriginMatch(QuotationItem i) => TxtMatch(i.Origin, lot.Origin);
        bool GradeMatch(QuotationItem i) => TxtMatch(i.Grade, lot.Grade);
        bool ThickMatch(QuotationItem i) => DimMatch(i.ThicknessValues, i.ThicknessMin, i.ThicknessMax, i.ThicknessOpen, lot.Thickness);
        bool WidthMatch(QuotationItem i) => DimMatch(i.WidthValues, i.WidthMin, i.WidthMax, i.WidthOpen, lot.Width);
        bool LengthMatch(QuotationItem i) => DimMatch(i.LengthValues, i.LengthMin, i.LengthMax, i.LengthOpen, lot.Length);
        // Item khớp mọi field KHÁC (trừ field đang gợi ý "skip").
        IEnumerable<QuotationItem> ItemsExcept(string skip) => MatchingQuotationItems(lot).Where(i =>
            (skip == "origin" || OriginMatch(i)) && (skip == "grade" || GradeMatch(i)) && (skip == "thick" || ThickMatch(i))
            && (skip == "width" || WidthMatch(i)) && (skip == "length" || LengthMatch(i)));
        // min=0 → cận dưới luôn MỞ (>) như matcher (kích thước gỗ = 0 vô nghĩa).
        string RangeHint(double? min, double? max, bool open) =>
            IntervalHint(min.HasValue ? Fmt.Num(min.Value) : null, max.HasValue ? Fmt.Num(max.Value) : null,
                open || (min.HasValue && Math.Abs(min.Value) < 1e-9), open);
        string FootageHint(string minNote, string maxNote, bool open) =>
            IntervalHint(string.IsNullOrWhiteSpace(minNote) ? null : minNote,
                string.IsNullOrWhiteSpace(maxNote) ? null : maxNote, open, open);
        List<string> OriginSuggest() => SuggestFrom(ItemsExcept("origin"), i => new[] { i.Origin });
        List<string> GradeSuggest() => SuggestFrom(ItemsExcept("grade"), i => new[] { i.Grade });
        List<string> ThickSuggest() => SuggestFrom(ItemsExcept("thick"), i => Footage()
            ? new[] { FootageHint(i.ThicknessMinNote, i.ThicknessMaxNote, i.ThicknessOpen) }
            : !string.IsNullOrWhiteSpace(i.ThicknessValues)
                ? Fmt.ParseValueList(i.ThicknessValues).Select(Fmt.Num)
                : new[] { i.ThicknessMin.HasValue || i.ThicknessMax.HasValue ? RangeHint(i.ThicknessMin, i.ThicknessMax, i.ThicknessOpen) : null });
        List<string> WidthSuggest() => SuggestFrom(ItemsExcept("width"), i => !string.IsNullOrWhiteSpace(i.WidthValues)
            ? Fmt.ParseValueList(i.WidthValues).Select(Fmt.Num)
            : new[] { i.WidthMin.HasValue || i.WidthMax.HasValue ? RangeHint(i.WidthMin, i.WidthMax, i.WidthOpen) : null });
        List<string> LengthSuggest() => SuggestFrom(ItemsExcept("length"), i => !string.IsNullOrWhiteSpace(i.LengthValues)
            ? Fmt.ParseValueList(i.LengthValues).Select(Fmt.Num)
            : new[] { i.LengthMin.HasValue || i.LengthMax.HasValue ? RangeHint(i.LengthMin, i.LengthMax, i.LengthOpen) : null });

        // Các ô kích thước (tạo trước để ẩn/hiện theo nguyên tắc tính m³) — Rộng/Dài có dropdown gợi ý.
        var widthBox = BuildSuggestCell(lot.Width, s => { lot.Width = s; Update(); }, WidthSuggest, out var widthTb, center: true);
        var lengthBox = BuildSuggestCell(lot.Length, s => { lot.Length = s; Update(); }, LengthSuggest, out var lengthTb, center: true);
        var lengthNoteBox = Cell(lot.LengthNote ?? "", s => { lot.LengthNote = s; }, mono: true, center: true);
        lengthNoteBox.Margin = new Thickness(6, 0, 6, 0);
        lengthNoteBox.ToolTip = Lang.T("Receipts.LengthNoteTooltip");
        var footageBox = Cell(lot.Footage, s => { lot.Footage = s; Update(); }, mono: true, center: true);
        footageBox.Margin = new Thickness(6, 0, 6, 0);

        // Quy cách → ẩn Footage. Footage → hiện Footage. Cột Rộng LUÔN cho nhập (kể cả footage, không bắt buộc).
        // Cột Dài: quy cách=mm, footage=inch.
        void ApplyRuleVisibility()
        {
            var footage = AppState.GetVolumeRule(lot.WoodType) == VolumeRule.ByFootage;
            widthBox.Visibility = Visibility.Visible;   // Rộng cho nhập ở mọi loại — footage không dùng để tính m³, chỉ lưu mô tả
            footageBox.Visibility = footage ? Visibility.Visible : Visibility.Collapsed;
            lengthBox.Visibility = footage ? Visibility.Collapsed : Visibility.Visible;
            lengthNoteBox.Visibility = footage ? Visibility.Visible : Visibility.Collapsed;
        }

        // 3. Loại gỗ (cha) + 4. Phân loại con (hai cột riêng, cha trái con phải)
        var typeCombo = new ComboBox
        {
            Style = (Style)FindResource("Select"),
            Height = 32, // khớp Height của CellInput/CellInputMono cùng hàng (Select mặc định 40 — quá cao cho bảng)
            Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !ReadOnly
        };
        var subCombo = new ComboBox
        {
            Style = (Style)FindResource("Select"),
            Height = 32,
            Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !ReadOnly
        };

        // Dropdown CHỈ liệt kê loại gỗ / phân loại con mà NCC đang chọn CÓ trong báo giá (xem WoodTypeChoices,
        // SubTypeChoices) — khỏi phải lội qua toàn bộ danh mục. Giá trị đang khai mà không còn trong danh sách
        // (đổi NCC, hoặc sửa phiếu cũ) vẫn được giữ lại làm 1 mục, không tự xoá dữ liệu người dùng.
        var syncingCombo = false;   // đang nạp lại Items → bỏ qua SelectionChanged do chính việc nạp gây ra

        void FillCombo(ComboBox combo, string placeholderKey, IEnumerable<string> choices, string current)
        {
            syncingCombo = true;
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = Lang.T(placeholderKey), Tag = "" });
            var list = choices.ToList();
            if (!string.IsNullOrWhiteSpace(current)
                && !list.Contains(current, StringComparer.OrdinalIgnoreCase)) list.Add(current);
            foreach (var v in list)
                combo.Items.Add(new ComboBoxItem { Content = v, Tag = v, IsSelected = v == current });
            if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
            syncingCombo = false;
        }

        void RebuildSub() => FillCombo(subCombo, "Receipts.SubTypePlaceholder", SubTypeChoices(lot.WoodType), lot.WoodSubType);
        void RebuildType() => FillCombo(typeCombo, "Receipts.WoodTypePlaceholder", WoodTypeChoices(), lot.WoodType);

        subCombo.SelectionChanged += (_, _) =>
        {
            if (syncingCombo) return;
            var v = (subCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            lot.WoodSubType = string.IsNullOrEmpty(v) ? null : v;
            Update();   // đổi phân loại con có thể đổi giá báo giá khớp được
        };
        typeCombo.SelectionChanged += (_, _) =>
        {
            if (syncingCombo) return;
            lot.WoodType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            lot.WoodSubType = null;      // đổi loại cha → reset phân loại con
            RebuildSub();
            ApplyRuleVisibility();
            Update();
        };

        RebuildType();
        RebuildSub();
        ApplyRuleVisibility();
        // Đổi NCC ở header → nạp lại 2 dropdown theo báo giá của NCC mới (giữ nguyên giá trị đang khai).
        _rowSupplierRebuilders.Add(() => { RebuildType(); RebuildSub(); ApplyRuleVisibility(); });

        Grid.SetColumn(typeCombo, 3); grid.Children.Add(typeCombo);
        Grid.SetColumn(subCombo, 4); grid.Children.Add(subCombo);

        // 5. Xuất xứ — ô nhập kèm dropdown gợi ý (tối đa 3) theo xuất xứ trong báo giá NCC khớp loại/phân loại con
        var originCell = BuildSuggestCell(lot.Origin, s => { lot.Origin = s; Update(); }, OriginSuggest, out var originTb, center: true);
        Grid.SetColumn(originCell, 5); grid.Children.Add(originCell);

        // 6. Chất lượng (Grade) — ô nhập kèm dropdown gợi ý theo grade trong báo giá NCC khớp
        var gradeCell = BuildSuggestCell(lot.Grade, s => { lot.Grade = s; Update(); }, GradeSuggest, out var gradeTb, center: true);
        Grid.SetColumn(gradeCell, 6); grid.Children.Add(gradeCell);

        // 7. Dày (gỗ footage chấp nhận ký hiệu inch: 1", 4/4", 5/4"...) — có dropdown gợi ý theo báo giá
        var thickBox = BuildSuggestCell(lot.Thickness, s => { lot.Thickness = s; Update(); }, ThickSuggest, out var thickTb,
            center: true, tooltip: Lang.T("Receipts.ThicknessTooltip"));
        Grid.SetColumn(thickBox, 7); grid.Children.Add(thickBox);

        // 8. Rộng (ẩn nếu gỗ footage)
        Grid.SetColumn(widthBox, 8); grid.Children.Add(widthBox);
        // 9. Dài (quy cách = mm; footage = ký hiệu inch — hai ô chồng nhau, toggle theo rule)
        Grid.SetColumn(lengthBox, 9); grid.Children.Add(lengthBox);
        Grid.SetColumn(lengthNoteBox, 9); grid.Children.Add(lengthNoteBox);
        // 10. Footage (ẩn nếu gỗ quy cách)
        Grid.SetColumn(footageBox, 10); grid.Children.Add(footageBox);

        // 11. Số lượng
        var qtyBox = Cell(lot.Quantity, s => { lot.Quantity = s; Update(); }, mono: true, center: true);
        qtyBox.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(qtyBox, 11); grid.Children.Add(qtyBox);

        // 12. Thể tích
        Grid.SetColumn(cbmText, 12); grid.Children.Add(cbmText);

        // 13. Số thập phân làm tròn m³ riêng dòng (mặc định 5)
        var decimalsBox = Cell(lot.VolumeDecimals.ToString(), s =>
        {
            lot.VolumeDecimals = Math.Clamp((int)D(s), 0, 15);
            Update();
        }, mono: true, center: true);
        decimalsBox.ToolTip = Lang.T("Receipts.Row.DecimalsTooltip");
        Grid.SetColumn(decimalsBox, 13); grid.Children.Add(decimalsBox);

        // 14. Điều chỉnh tay +/- cộng vào m³ sau khi làm tròn
        var adjustmentBox = Cell(lot.VolumeAdjustment == 0 ? "" : Fmt.Num(lot.VolumeAdjustment), s =>
        {
            lot.VolumeAdjustment = D(s);
            Update();
        }, mono: true);
        adjustmentBox.TextAlignment = TextAlignment.Right;
        adjustmentBox.ToolTip = Lang.T("Receipts.Row.AdjustmentTooltip");
        Grid.SetColumn(adjustmentBox, 14); grid.Children.Add(adjustmentBox);

        // 15. Đơn giá  16-19. Tổng tiền gốc/VND, Tiền thuế, Tổng cộng
        Grid.SetColumn(priceBox, 15); grid.Children.Add(priceBox);
        Grid.SetColumn(priceHint, 15); grid.Children.Add(priceHint);
        Grid.SetColumn(mismatchIcon, 15); grid.Children.Add(mismatchIcon);   // đè mép trái ô đơn giá
        Grid.SetColumn(totalUsdText, 16); grid.Children.Add(totalUsdText);
        Grid.SetColumn(totalVndText, 17); grid.Children.Add(totalVndText);
        Grid.SetColumn(taxVndText, 18); grid.Children.Add(taxVndText);
        Grid.SetColumn(grandTotalText, 19); grid.Children.Add(grandTotalText);

        // Xóa
        var del = new Button
        {
            Style = (Style)FindResource("BtnIconDanger"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Visibility = ReadOnly ? Visibility.Collapsed : Visibility.Visible,
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12 }
        };
        del.Click += (_, _) =>
        {
            _draftLots.Remove(lot);
            RebuildLotRows();
        };
        Grid.SetColumn(del, 20); grid.Children.Add(del);

        // Điều hướng bàn phím: 4 mũi tên chuyển ô, Ctrl+D chép ô cùng cột dòng trên. Bảng này dựng tay (không phải
        // DataGrid) nên phải tự khai bản đồ cột → ô nhập. Cột 9 có 2 ô (Dài mm / Dài inch), chỉ 1 cái hiện theo loại gỗ.
        var nav = new RowNav();
        nav.Add(1, idBox); nav.Add(2, deliveryNoteBox);
        nav.Add(3, typeCombo); nav.Add(4, subCombo);
        nav.Add(5, originTb); nav.Add(6, gradeTb); nav.Add(7, thickTb); nav.Add(8, widthTb);
        nav.Add(9, lengthTb); nav.Add(9, lengthNoteBox); nav.Add(10, footageBox);
        nav.Add(11, qtyBox); nav.Add(13, decimalsBox); nav.Add(14, adjustmentBox); nav.Add(15, priceBox);
        AttachRowNav(nav, stt - 1);

        var row = new Border
        {
            BorderBrush = (Brush)FindResource("Slate100"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.White,
            Child = grid
        };
        WireRowHighlight(row, lot);
        return row;
    }

    private readonly List<Action> _rowUpdaters = new();

    /// <summary>Nạp lại dropdown Loại gỗ/Phân loại con của từng dòng khi đổi NCC (danh sách phụ thuộc báo giá NCC).</summary>
    private readonly List<Action> _rowSupplierRebuilders = new();

    // ---------------- Hover + chọn dòng cho bảng kiện (dựng tay nên không có sẵn như DataGrid) ----------------

    private DraftLot _selectedLot;                       // dòng kiện đang được chọn (tô nền xanh mờ)
    private readonly List<Action> _rowHighlighters = new();   // cập nhật nền của từng dòng

    /// <summary>
    /// Nối hover + chọn dòng cho 1 dòng kiện, dùng đúng 2 màu của DataGrid (hover Slate50, chọn RowSelected)
    /// để nhìn giống mọi bảng khác. Chọn được cả bằng chuột lẫn khi focus bàn phím rơi vào dòng (mũi tên, Tab).
    /// </summary>
    private void WireRowHighlight(Border row, DraftLot lot)
    {
        void Apply() => row.Background = _selectedLot == lot
            ? (Brush)FindResource("RowSelected")
            : row.IsMouseOver ? (Brush)FindResource("Slate50") : Brushes.White;

        _rowHighlighters.Add(Apply);
        row.MouseEnter += (_, _) => Apply();
        row.MouseLeave += (_, _) => Apply();
        row.PreviewMouseLeftButtonDown += (_, _) => SelectLotRow(lot);
        row.IsKeyboardFocusWithinChanged += (_, e) => { if (e.NewValue is true) SelectLotRow(lot); };
        Apply();
    }

    private void SelectLotRow(DraftLot lot)
    {
        if (ReferenceEquals(_selectedLot, lot)) return;
        _selectedLot = lot;
        foreach (var apply in _rowHighlighters.ToList()) apply();
    }

    // ---------------- Điều hướng bàn phím trong bảng khai kiện ----------------
    // Bảng kiện dựng tay nên không có sẵn phím kiểu DataGrid → tự nối: 4 mũi tên chuyển ô, Ctrl+D chép ô cùng cột dòng trên.

    /// <summary>Bản đồ cột → các ô nhập của MỘT dòng kiện (1 cột có thể có 2 ô chồng nhau, chỉ 1 cái hiện).</summary>
    private sealed class RowNav
    {
        private const int MaxColumn = 20;
        public readonly Dictionary<int, List<FrameworkElement>> Cells = new();

        public void Add(int col, FrameworkElement cell)
        {
            if (!Cells.TryGetValue(col, out var list)) Cells[col] = list = new List<FrameworkElement>();
            list.Add(cell);
        }

        /// <summary>Ô đang HIỆN + nhập được của cột (null nếu cột không có ô nhập, hoặc ô đang ẩn theo nguyên tắc tính m³).</summary>
        public FrameworkElement Focusable(int col) =>
            col >= 0 && col <= MaxColumn && Cells.TryGetValue(col, out var list)
                ? list.FirstOrDefault(c => c.IsVisible && c.IsEnabled && c is not TextBox { IsReadOnly: true })
                : null;

        public static int LastColumn => MaxColumn;
    }

    private readonly List<RowNav> _rowNavs = new();

    /// <summary>Đang chuyển ô bằng mũi tên → chặn dropdown gợi ý tự bung (xem Refresh trong BuildSuggestCell).</summary>
    private bool _navFocusing;

    private void AttachRowNav(RowNav nav, int rowIndex)
    {
        while (_rowNavs.Count <= rowIndex) _rowNavs.Add(null);
        _rowNavs[rowIndex] = nav;
        foreach (var (col, list) in nav.Cells)
            foreach (var cell in list)
            {
                var c = col;
                var fe = cell;
                fe.PreviewKeyDown += (_, e) => LotCell_KeyDown(fe, rowIndex, c, e);
            }
    }

    private void LotCell_KeyDown(FrameworkElement cell, int row, int col, KeyEventArgs e)
    {
        if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = FillDownFromAbove(row, col);
            return;
        }

        int dRow = 0, dCol = 0;
        switch (e.Key)
        {
            case Key.Up: dRow = -1; break;
            case Key.Down: dRow = 1; break;
            case Key.Left: dCol = -1; break;
            case Key.Right: dCol = 1; break;
            default: return;
        }
        if (!CanLeaveCell(cell, dCol)) return;
        e.Handled = dRow != 0 ? FocusCell(row + dRow, col, caretAtEnd: true) : MoveHorizontal(row, col, dCol);
    }

    /// <summary>
    /// Mũi tên chỉ được "nhả" ra khỏi ô khi không cướp thao tác gốc: TextBox chỉ nhả sang trái/phải khi con trỏ
    /// đã ở đầu/cuối text và không bôi đen (còn lại vẫn di chuyển con trỏ như thường); ComboBox chỉ nhả khi
    /// dropdown ĐANG ĐÓNG (đang mở thì mũi tên là chọn item).
    /// </summary>
    private static bool CanLeaveCell(FrameworkElement cell, int dCol) => cell switch
    {
        ComboBox cb => !cb.IsDropDownOpen,
        TextBox tb when dCol < 0 => tb.SelectionLength == 0 && tb.CaretIndex == 0,
        TextBox tb when dCol > 0 => tb.SelectionLength == 0 && tb.CaretIndex == (tb.Text ?? "").Length,
        _ => true
    };

    /// <summary>Sang trái/phải: bỏ qua các cột không có ô nhập (STT, thể tích, tiền, nút xóa) và ô đang ẩn.</summary>
    private bool MoveHorizontal(int row, int col, int step)
    {
        if (row < 0 || row >= _rowNavs.Count || _rowNavs[row] == null) return false;
        for (var c = col + step; c >= 0 && c <= RowNav.LastColumn; c += step)
            if (_rowNavs[row].Focusable(c) != null)
                return FocusCell(row, c, caretAtEnd: step < 0);
        return false;
    }

    /// <summary>Đưa focus tới ô (dòng, cột); đi sang TRÁI/lên/xuống thì đặt con trỏ cuối text, sang PHẢI thì đầu text.</summary>
    private bool FocusCell(int row, int col, bool caretAtEnd)
    {
        if (row < 0 || row >= _rowNavs.Count || _rowNavs[row] == null) return false;
        var target = _rowNavs[row].Focusable(col);
        if (target == null) return false;

        _navFocusing = true;
        target.Focus();
        if (target is TextBox tb)
        {
            tb.SelectionLength = 0;
            tb.CaretIndex = caretAtEnd ? (tb.Text ?? "").Length : 0;
        }
        target.BringIntoView();
        // Nhả cờ SAU khi dropdown gợi ý đã kịp kiểm tra (Refresh chạy ở mức Input, cao hơn Background).
        Dispatcher.BeginInvoke(new Action(() => _navFocusing = false),
            System.Windows.Threading.DispatcherPriority.Background);
        return true;
    }

    /// <summary>Ctrl+D: chép nội dung ô cùng cột ở dòng NGAY TRÊN xuống ô hiện tại (kiểu fill-down của Excel).</summary>
    private bool FillDownFromAbove(int row, int col)
    {
        if (row <= 0 || row >= _rowNavs.Count) return false;
        var src = _rowNavs[row - 1]?.Focusable(col);
        var dst = _rowNavs[row]?.Focusable(col);
        if (src == null || dst == null) return false;

        switch (src, dst)
        {
            case (TextBox from, TextBox to):
                to.Text = from.Text;              // TextChanged tự cập nhật DraftLot + tính lại dòng
                to.CaretIndex = (to.Text ?? "").Length;
                return true;
            case (ComboBox from, ComboBox to):
                var tag = (from.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                foreach (ComboBoxItem it in to.Items)
                    if ((it.Tag as string ?? "") == tag) { to.SelectedItem = it; return true; }
                return false;
            default:
                return false;
        }
    }

    private TextBox Cell(string initial, Action<string> onChange, bool mono = false, bool center = false, bool bold = false)
    {
        var box = new TextBox
        {
            Style = (Style)FindResource(mono ? "CellInputMono" : "CellInput"),
            Text = initial,
            VerticalAlignment = VerticalAlignment.Center,
            IsReadOnly = ReadOnly
        };
        if (ReadOnly) box.Background = (Brush)FindResource("Slate50");
        if (center) box.TextAlignment = TextAlignment.Center;
        if (bold) box.FontWeight = FontWeights.SemiBold;
        // UpdateSupplierLock: ô như Mã kiện/Phiếu giao hàng không đi qua Update() nên phải tự báo "đã khai kiện".
        box.TextChanged += (_, _) => { onChange(box.Text); UpdateSupplierLock(); };
        return box;
    }

    /// <summary>Toàn bộ dòng giá trong báo giá của NCC đang chọn (rỗng nếu chưa chọn NCC / NCC chưa có báo giá).</summary>
    private List<QuotationItem> SupplierQuotationItems()
    {
        var supId = SelectedSupplierId;
        if (string.IsNullOrEmpty(supId)) return new();
        return AppState.FindQuotation(supId)?.Items?.ToList() ?? new();
    }

    /// <summary>
    /// Loại gỗ (cha) cho dropdown khai kiện: CHỈ những loại NCC đang chọn có trong báo giá, giữ thứ tự của danh mục.
    /// Chưa chọn NCC / NCC chưa có báo giá nào → trả TOÀN BỘ danh mục (không thì không khai nổi kiện).
    /// </summary>
    private List<string> WoodTypeChoices()
    {
        var quoted = SupplierQuotationItems()
            .Select(i => i.WoodType).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (quoted.Count == 0) return AppState.CategoryNames.ToList();
        return AppState.CategoryNames.Where(c => quoted.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Phân loại con cho dropdown khai kiện: CHỈ những con NCC có báo giá dưới loại cha đó.
    /// Báo giá chỉ khai ở cấp cha (con để trống) → trả đủ con của danh mục, vì ở Nhập Kho con là BẮT BUỘC khi cha có con.
    /// </summary>
    private List<string> SubTypeChoices(string woodType)
    {
        if (string.IsNullOrWhiteSpace(woodType)) return new();
        var all = AppState.SubNamesOf(woodType).ToList();
        var quoted = SupplierQuotationItems()
            .Where(i => string.Equals(i.WoodType, woodType, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.WoodSubType).Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return quoted.Count == 0 ? all : all.Where(s => quoted.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Các dòng giá của NCC khớp loại gỗ cha + phân loại con của kiện (dòng cấp cha — sub trống — cũng tính).</summary>
    private List<QuotationItem> MatchingQuotationItems(DraftLot lot)
    {
        var supId = SelectedSupplierId;
        if (string.IsNullOrEmpty(supId) || string.IsNullOrWhiteSpace(lot.WoodType)) return new();
        var quotation = AppState.FindQuotation(supId);
        if (quotation == null) return new();
        return quotation.Items
            .Where(i => string.Equals(i.WoodType, lot.WoodType, StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(i.WoodSubType)
                            || string.Equals(i.WoodSubType, lot.WoodSubType, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>Sinh chuỗi hint dải hợp lệ từ 2 mốc đã format: "a≤x≤b" (đoạn) / "a&lt;x&lt;b" (khoảng) /
    /// "x≥a" / "x≤b" / "x&gt;a" / "x&lt;b" (nửa) / "a" (đơn lẻ min=max) / null (trống).
    /// <paramref name="lowerOpen"/>/<paramref name="upperOpen"/> = cận tương ứng MỞ (dùng &lt;/&gt; thay ≤/≥).</summary>
    private static string IntervalHint(string minStr, string maxStr, bool lowerOpen, bool upperOpen)
    {
        if (minStr == null && maxStr == null) return null;
        if (minStr != null && maxStr != null)
            return minStr == maxStr ? minStr
                 : $"{minStr}{(lowerOpen ? "<" : "≤")}x{(upperOpen ? "<" : "≤")}{maxStr}";
        if (minStr != null) return $"x{(lowerOpen ? ">" : "≥")}{minStr}";
        return $"x{(upperOpen ? "<" : "≤")}{maxStr}";
    }

    /// <summary>Số gợi ý hiện mặc định; dư ra thì gói phần còn lại sau dòng "…".</summary>
    private const int SuggestPageSize = 5;

    /// <summary>Dòng cuối "xem thêm" trong danh sách gợi ý — bấm vào để bung hết, KHÔNG phải một giá trị chọn được.</summary>
    private const string SuggestMoreMarker = "…";

    /// <summary>Chuẩn hóa danh sách gợi ý: bỏ rỗng, bỏ trùng (không phân biệt hoa thường), sắp xếp.</summary>
    private static List<string> DistinctSuggest(IEnumerable<string> values) =>
        values.Select(v => (v ?? "").Trim()).Where(v => v.Length > 0)
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// Ô nhập kèm dropdown gợi ý (tối đa 10) + cho phép gõ để lọc; TỰ BUNG khi focus (kể cả chưa gõ).
    /// Trả về Grid bọc TextBox + Popup (Popup nổi, không ảnh hưởng layout ô); <paramref name="cellBox"/> trả ra
    /// chính TextBox bên trong để nối phím điều hướng (xem RowNav).
    /// </summary>
    private FrameworkElement BuildSuggestCell(string initial, Action<string> onChange, Func<List<string>> suggest,
        out TextBox cellBox, bool center = false, string tooltip = null)
    {
        var box = new TextBox
        {
            Style = (Style)FindResource("CellInputMono"),
            Text = initial,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            IsReadOnly = ReadOnly
        };
        if (center) box.TextAlignment = TextAlignment.Center;
        if (tooltip != null) box.ToolTip = tooltip;
        if (ReadOnly) box.Background = (Brush)FindResource("Slate50");

        var list = new ListBox { BorderThickness = new Thickness(0), MaxHeight = 220, FontSize = 12 };
        // Dòng "…" (xem thêm) hiện khác hẳn giá trị thật: chữ xám, căn giữa, để user biết đây là nút mở rộng.
        var moreStyle = new Style(typeof(ListBoxItem), (Style)TryFindResource(typeof(ListBoxItem)));
        var moreTrigger = new DataTrigger { Binding = new Binding("."), Value = SuggestMoreMarker };
        moreTrigger.Setters.Add(new Setter(ForegroundProperty, (Brush)FindResource("Slate400")));
        moreTrigger.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        moreTrigger.Setters.Add(new Setter(FontWeightProperty, FontWeights.Bold));
        moreTrigger.Setters.Add(new Setter(ToolTipProperty, Lang.T("Receipts.Suggest.ShowAll")));
        moreStyle.Triggers.Add(moreTrigger);
        list.ItemContainerStyle = moreStyle;
        var popup = new Popup
        {
            PlacementTarget = box,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = (Brush)FindResource("Slate200"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                MinWidth = 150,
                Margin = new Thickness(0, 2, 0, 8),
                Child = list
            }
        };

        var suppress = false;
        var fullList = new List<string>();   // danh sách gợi ý ĐẦY ĐỦ của lần lọc gần nhất (để bung khi bấm "…")

        void Refresh()
        {
            // Đang nhảy ô bằng mũi tên → KHÔNG tự bung gợi ý (bung liên tục mỗi lần lướt qua ô rất nhiễu);
            // muốn xem gợi ý thì gõ tiếp hoặc bấm Alt.
            if (ReadOnly || _navFocusing) { popup.IsOpen = false; return; }
            var typed = (box.Text ?? "").Trim();
            var sugg = suggest()
                .Where(o => typed.Length == 0 || o.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            // Không mở nếu không có gợi ý, hoặc gợi ý duy nhất trùng khít text đang gõ.
            if (sugg.Count == 0 || (sugg.Count == 1 && string.Equals(sugg[0], typed, StringComparison.OrdinalIgnoreCase)))
            {
                popup.IsOpen = false;
                return;
            }
            // Mặc định chỉ 5 dòng; dư ra thì dòng cuối là "…" — bấm/Enter vào nó để xem TOÀN BỘ (xem Pick).
            fullList = sugg;
            var items = new List<string>(sugg);
            if (items.Count > SuggestPageSize)
            {
                items = items.Take(SuggestPageSize).ToList();
                items.Add(SuggestMoreMarker);
            }
            list.ItemsSource = items;
            popup.IsOpen = box.IsKeyboardFocused || box.IsKeyboardFocusWithin;
        }

        void Pick(string s)
        {
            // Bấm "…" = xem hết phần còn lại, KHÔNG phải chọn giá trị → gán THẲNG danh sách đầy đủ, giữ popup mở.
            // Cố ý KHÔNG gọi lại Refresh (nó sẽ cắt về 5 dòng ngay) và KHÔNG đụng focus (đang ở list hay ở ô đều được).
            if (s == SuggestMoreMarker)
            {
                list.ItemsSource = new List<string>(fullList);
                popup.IsOpen = true;
                return;
            }
            suppress = true;
            box.Text = s;
            box.CaretIndex = s.Length;
            suppress = false;
            popup.IsOpen = false;
            box.Focus();
        }

        // Bung khi focus: defer qua Dispatcher để không bị chính cú click focus đóng popup ngay (StaysOpen=false).
        box.GotKeyboardFocus += (_, _) =>
            box.Dispatcher.BeginInvoke(new Action(Refresh), System.Windows.Threading.DispatcherPriority.Input);
        box.TextChanged += (_, _) => { onChange(box.Text); UpdateSupplierLock(); if (!suppress) Refresh(); };
        box.LostKeyboardFocus += (_, _) => { if (!list.IsKeyboardFocusWithin) popup.IsOpen = false; };
        // Bung danh sách gợi ý + nhảy vào item đầu (mũi tên trơn đã dành cho chuyển ô — xem LotCell_KeyDown).
        void OpenSuggest()
        {
            if (!popup.IsOpen) Refresh();
            if (!popup.IsOpen || list.Items.Count == 0) return;
            box.Dispatcher.BeginInvoke(new Action(() =>
            {
                list.SelectedIndex = 0;
                (list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        // Bấm Alt MỘT MÌNH (nhấn rồi nhả, không kèm phím khác) = bung gợi ý. Alt trong app không dùng cho việc gì
        // khác (không có menu, không có access key `_` trong bản dịch) nên không giẫm chân ai. Alt+↓ vẫn nhận.
        var altAlone = false;
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            altAlone = key is Key.LeftAlt or Key.RightAlt;   // phím khác (kể cả Alt+X) → không còn là Alt đơn
            if (key == Key.Down && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                OpenSuggest();
                e.Handled = true;
            }
            else if (key == Key.Escape && popup.IsOpen) { popup.IsOpen = false; e.Handled = true; }
        };
        box.PreviewKeyUp += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (!altAlone || key is not (Key.LeftAlt or Key.RightAlt)) return;
            altAlone = false;
            OpenSuggest();
            e.Handled = true;   // chặn luôn chế độ "menu/gạch chân access key" mặc định của Alt
        };
        list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem { Content: string s })
                Pick(s);
        };
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && list.SelectedItem is string s) { Pick(s); e.Handled = true; }
            else if (e.Key == Key.Escape) { popup.IsOpen = false; box.Focus(); e.Handled = true; }
        };

        var host = new Grid();
        host.Children.Add(box);
        host.Children.Add(popup);
        cellBox = box;
        return host;
    }

    private void UpdateTotals()
    {
        UpdateSupplierLock();   // khai/xoá kiện làm đổi quyền đổi NCC
        foreach (var lot in _draftLots) Recalculate(lot);
        TotalCbm.Text = $"{Fmt.M3Total(_draftLots.Sum(l => l.Cbm))} m³";
        TotalValue.Text = Fmt.Vnd(_draftLots.Sum(l => l.TotalValueVnd));
    }

    private void FSupplier_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        // Đổi NCC → danh sách loại gỗ/phân loại con trong dropdown đổi theo báo giá của NCC mới, rồi mới tính lại giá.
        foreach (var rebuild in _rowSupplierRebuilders.ToList()) rebuild();
        foreach (var update in _rowUpdaters.ToList()) update();
    }

    /// <summary>Tỷ giá/Thuế chung của phiếu đổi → tính lại giá vốn cho mọi kiện đang khai báo.</summary>
    private void FRate_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        foreach (var update in _rowUpdaters.ToList()) update();
    }

    private void BtnAddLotRow_Click(object sender, RoutedEventArgs e)
    {
        _draftLots.Add(new DraftLot());   // dòng mới: mọi field trống
        RebuildLotRows();
    }

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        // Đã ở add mode → không làm gì (bỏ hành vi click lần 2 đóng form, gây mất dữ liệu chưa lưu không cảnh báo)
        if (AddFormPanel.Visibility == Visibility.Visible && _mode == "add") return;
        if (!ConfirmLeaveDirty()) return;   // đang sửa (có thay đổi chưa lưu) → xác nhận trước khi sang lập phiếu mới
        // Còn lại (đang ẩn, hoặc đang xem/sửa) → chuyển thẳng sang lập phiếu mới
        EnterAddMode();
        AddFormPanel.Visibility = Visibility.Visible;
    }

    // ---------------- Trang Bảng tổng chi tiết nhập kho (drill-down trong cùng tab) ----------------

    private void BtnOpenReport_Click(object sender, RoutedEventArgs e)
    {
        AddFormPanel.Visibility = Visibility.Collapsed;   // đóng form lập phiếu nếu đang mở
        EnterAddMode();

        _reportView = new ReceiptReportView(BackToReceipts);
        DetailHost.Content = _reportView;
        ListRoot.Visibility = Visibility.Collapsed;
        DetailHost.Visibility = Visibility.Visible;
        Main?.SetBreadcrumbDetail(Lang.T("Receipts.OpenReportButton"), BackToReceipts);
    }

    private void BackToReceipts()
    {
        _reportView = null;
        DetailHost.Content = null;
        DetailHost.Visibility = Visibility.Collapsed;
        ListRoot.Visibility = Visibility.Visible;
        Main?.SetBreadcrumbDetail(null);
        RefreshView();
    }

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
    {
        // Đang sửa → xác nhận hủy, bỏ thay đổi và quay lại xem chi tiết (không lưu)
        if (_mode == "edit")
        {
            if (!ConfirmDiscard(Lang.T("Common.Confirm.DiscardEdit"))) return;
            var r = AppState.Receipts.FirstOrDefault(x => x.Id == _editingReceiptId);
            if (r != null) { EnterViewMode(r); return; }
        }
        // Đang thêm mới → xác nhận trước khi bỏ thông tin đã nhập
        else if (_mode == "add")
        {
            if (!ConfirmDiscard(Lang.T("Common.Confirm.DiscardAdd"))) return;
        }
        AddFormPanel.Visibility = Visibility.Collapsed;
        EnterAddMode();
    }

    /// <summary>Hộp thoại xác nhận hủy (thông điệp tùy chế độ add/edit).</summary>
    private static bool ConfirmDiscard(string message) =>
        AppDialog.Show(message, Lang.T("Common.ConfirmDiscardTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>Đang MỞ form add/edit (dữ liệu chưa lưu) → hỏi xác nhận bỏ trước khi rời sang xem/sửa dòng khác (true = được rời).</summary>
    private bool ConfirmLeaveDirty() =>
        AddFormPanel.Visibility != Visibility.Visible || (_mode != "add" && _mode != "edit")
        || ConfirmDiscard(Lang.T(_mode == "add" ? "Common.Confirm.DiscardAdd" : "Common.Confirm.DiscardEdit"));

    // ---------------- Chế độ add / view / edit ----------------

    /// <summary>Bật/tắt chỉ-đọc cho các ô ở phần header chứng từ.</summary>
    private void SetHeaderReadOnly(bool ro)
    {
        foreach (var box in new[] { FInvoice, FPackingList, FForestList, FExchangeRate })
        {
            box.IsReadOnly = ro;
            box.Background = ro ? (Brush)FindResource("Slate50") : Brushes.White;
        }
        FDate.IsEnabled = !ro;
        FTaxPercent.IsEnabled = !ro;
        ApplyCurrencyLock();    // Đơn vị tiền tệ + Tỷ giá: xem thì khoá cả hai, sửa thì theo luật VND
        UpdateSupplierLock();   // NCC có luật khoá riêng, chặt hơn cờ chỉ-đọc chung
    }

    /// <summary>Một dòng kiện coi như CHƯA khai nếu mọi ô còn trống (đúng trạng thái dòng nháp mặc định).</summary>
    private static bool IsBlankLot(DraftLot l) =>
        string.IsNullOrWhiteSpace(l.Id) && string.IsNullOrWhiteSpace(l.DeliveryNote)
        && string.IsNullOrWhiteSpace(l.WoodType) && string.IsNullOrWhiteSpace(l.WoodSubType)
        && string.IsNullOrWhiteSpace(l.Thickness) && string.IsNullOrWhiteSpace(l.Origin)
        && string.IsNullOrWhiteSpace(l.Grade) && string.IsNullOrWhiteSpace(l.Width)
        && string.IsNullOrWhiteSpace(l.Length) && string.IsNullOrWhiteSpace(l.LengthNote)
        && string.IsNullOrWhiteSpace(l.Footage) && string.IsNullOrWhiteSpace(l.Quantity)
        && string.IsNullOrWhiteSpace(l.ManualPriceText) && l.VolumeAdjustment == 0;

    /// <summary>
    /// Khoá combo NCC khi không được đổi nữa: chế độ SỬA (và xem) khoá HOÀN TOÀN — kiện của phiếu đã gắn NCC cũ;
    /// chế độ THÊM khoá ngay khi đã khai ít nhất 1 kiện — đổi NCC giữa chừng làm giá khớp + danh sách loại gỗ/phân
    /// loại con trong dropdown lệch hết với thứ vừa nhập. Muốn đổi NCC thì xoá hết dòng kiện đã khai.
    /// </summary>
    private void UpdateSupplierLock()
    {
        var locked = _mode != "add" || _draftLots.Any(l => !IsBlankLot(l));
        FSupplier.IsEnabled = !locked;
        // Style "Select" không có trigger IsEnabled → tự tô nền xám cho thấy đang khoá (giống ô Tỷ giá khi chọn VND).
        FSupplier.Background = locked ? (Brush)FindResource("Slate50") : Brushes.White;
        ToolTipService.SetShowOnDisabled(FSupplier, true);   // control bị disable vẫn phải hiện được lý do
        FSupplier.ToolTip = !locked || _mode == "view" ? null
            : Lang.T(_mode == "edit" ? "Receipts.SupplierLocked.Edit" : "Receipts.SupplierLocked.Add");
    }

    private void EnterAddMode()
    {
        _mode = "add";
        _editingReceiptId = null;
        SetHeaderReadOnly(false);
        BtnAddLotRow.Visibility = Visibility.Visible;
        FormTitle.Text = Lang.T("Receipts.Form.AddTitle");
        FormSaveBtn.Content = Lang.T("Receipts.SaveButton");
        FormCancelBtn.Content = Lang.T("Common.Cancel");
        FInvoice.Text = "";
        FPackingList.Text = "";
        FForestList.Text = "";
        FCurrency.SelectedIndex = 0;   // mặc định USD (khoá/mở tỷ giá do ApplyCurrencyLock lo)
        FExchangeRate.Text = Fmt.Num((double)AppState.Settings.DefaultExchangeRate);
        SelectTaxPercent(AppState.Settings.DefaultTaxPercent);
        FDate.SelectedDate = DateTime.Today;
        if (FSupplier.Items.Count > 0) FSupplier.SelectedIndex = 0;
        ResetDraft();
    }

    /// <summary>Xem chi tiết: nạp dữ liệu phiếu vào form ở chế độ chỉ-đọc, nút thành "Chỉnh sửa".</summary>
    private void EnterViewMode(WarehouseReceipt r)
    {
        _mode = "view";
        _editingReceiptId = r.Id;
        LoadReceiptIntoForm(r);
        SetHeaderReadOnly(true);
        BtnAddLotRow.Visibility = Visibility.Collapsed;
        FormTitle.Text = Lang.T("Receipts.Form.ViewTitle", r.Id);
        FormSaveBtn.Content = Lang.T("Common.Edit");
        FormCancelBtn.Content = Lang.T("Common.Close");
        AddFormPanel.Visibility = Visibility.Visible;
        UiScroll.ToTop(AddFormPanel);   // luôn kéo lên đầu trang để thấy form xem (kể cả khi đang xem dòng khác)
    }

    /// <summary>Chuyển sang sửa (từ xem hoặc trực tiếp): mở khóa, nút thành "Cập nhật".</summary>
    private void EnterEditMode(WarehouseReceipt r = null)
    {
        _mode = "edit";
        if (r != null) { _editingReceiptId = r.Id; LoadReceiptIntoForm(r); }
        SetHeaderReadOnly(false);
        RebuildLotRows();                    // dựng lại các dòng ở chế độ có thể sửa
        BtnAddLotRow.Visibility = Visibility.Visible;
        FormTitle.Text = Lang.T("Receipts.Form.EditTitle", _editingReceiptId);
        FormSaveBtn.Content = Lang.T("Common.Update");
        FormCancelBtn.Content = Lang.T("Common.CancelEdit");
        AddFormPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Nạp header + danh sách kiện của một phiếu vào form.</summary>
    private void LoadReceiptIntoForm(WarehouseReceipt r)
    {
        foreach (ComboBoxItem it in FSupplier.Items)
            if ((it.Tag as string) == r.SupplierId) { FSupplier.SelectedItem = it; break; }
        FDate.SelectedDate = r.Date;
        FInvoice.Text = r.Invoice;
        FPackingList.Text = r.PackingList;

        var first = r.Lots.FirstOrDefault();
        FForestList.Text = first?.ForestList ?? "";
        if (first != null)
        {
            FCurrency.SelectedIndex = string.Equals(first.PriceCurrency, "VND", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            FExchangeRate.Text = Fmt.Num((double)first.ExchangeRate);   // ghi đè giá trị mặc định mà FCurrency_Changed vừa set
            SelectTaxPercent(first.TaxPercent);
        }

        _draftLots.Clear();
        foreach (var l in r.Lots) _draftLots.Add(ToDraft(l));
        if (_draftLots.Count == 0) _draftLots.Add(new DraftLot());
        RebuildLotRows();
    }

    private static DraftLot ToDraft(WoodLot l)
    {
        var isFootage = AppState.GetVolumeRule(l.WoodType) == VolumeRule.ByFootage;
        return new DraftLot
        {
            Id = l.Id,
            DeliveryNote = l.DeliveryNote ?? "",
            WoodType = l.WoodType,
            WoodSubType = l.WoodSubType,
            // Gỗ footage: hiện lại ký hiệu inch gốc nếu có, không thì rơi về mm.
            Thickness = isFootage && !string.IsNullOrWhiteSpace(l.ThicknessNote)
                ? l.ThicknessNote : Fmt.Num(l.ThicknessMm),
            Origin = l.Origin ?? "",
            Grade = l.Grade ?? "",
            Width = l.WidthMm > 0 ? Fmt.Num(l.WidthMm) : "",   // để trống nếu chưa nhập (gỗ footage có thể bỏ rộng)
            Length = Fmt.Num(l.LengthMm),
            LengthNote = l.LengthNote,
            Footage = Fmt.Num(l.Footage),
            Quantity = l.OriginalQuantity.ToString(),
            ManualPriceText = Fmt.Num((double)l.Price),
            VolumeDecimals = l.VolumeDecimals ?? 5,
            VolumeAdjustment = l.VolumeAdjustment ?? 0
        };
    }

    // ---------------- Xử lý nút thao tác trên bảng ----------------

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecRow r) return;
        if (!ConfirmLeaveDirty()) return;
        EnterViewMode(r.Receipt);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecRow r) return;
        if (AppDialog.Show(
                Lang.T("Receipts.Confirm.DeleteReceipt", r.Id, r.Receipt.Lots.Count),
                Lang.T("Common.ConfirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            AppState.DeleteReceipt(r.Id);
            if (_editingReceiptId == r.Id) { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
        }
        catch (Exception ex)
        {
            AppDialog.Show(ex.Message, Lang.T("Common.CannotDeleteTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnSaveReceipt_Click(object sender, RoutedEventArgs e)
    {
        // Đang xem chi tiết → bấm "Chỉnh sửa" thì chuyển sang chế độ sửa
        if (_mode == "view") { EnterEditMode(); return; }

        var supplierId = SelectedSupplierId;
        var invoice = (FInvoice.Text ?? "").Trim();
        var packingList = (FPackingList.Text ?? "").Trim();
        var forestList = (FForestList.Text ?? "").Trim();

        if (supplierId.Length == 0 || invoice.Length == 0)
        {
            AppDialog.Show(Lang.T("Receipts.Warn.MissingDoc"), Lang.T("Common.AppTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (SelectedExchangeRate <= 0)
        {
            AppDialog.Show(Lang.T("Receipts.Warn.RateInvalid"), Lang.T("Common.AppTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_draftLots.Count == 0)
        {
            AppDialog.Show(Lang.T("Receipts.Warn.NoLots"), Lang.T("Common.AppTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var date = FDate.SelectedDate ?? DateTime.Today;

        var ids = _draftLots.Select(l => (l.Id ?? "").Trim().ToUpperInvariant()).ToList();
        var duplicates = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            AppDialog.Show(
                Lang.T("Receipts.Warn.DuplicateLotId", string.Join(", ", duplicates)),
                Lang.T("Common.AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var d in _draftLots)
        {
            if (string.IsNullOrWhiteSpace(d.Id))
            {
                AppDialog.Show(Lang.T("Receipts.Warn.LotIdRequired"), Lang.T("Common.AppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(d.WoodType))
            {
                AppDialog.Show(Lang.T("Receipts.Warn.WoodTypeRequired", d.Id), Lang.T("Common.AppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (AppState.CategoryHasSubs(d.WoodType) && string.IsNullOrWhiteSpace(d.WoodSubType))
            {
                AppDialog.Show(Lang.T("Receipts.Warn.SubTypeRequired", d.Id, d.WoodType),
                    Lang.T("Common.AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (ParseThickness(d) <= 0)
            {
                AppDialog.Show(Lang.T("Receipts.Warn.ThicknessRequired", d.Id), Lang.T("Common.AppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (AppState.GetVolumeRule(d.WoodType) == VolumeRule.ByFootage)
            {
                if (D(d.Footage) <= 0)
                {
                    AppDialog.Show(Lang.T("Receipts.Warn.FootageRequired", d.Id, d.WoodType),
                        Lang.T("Common.AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (D(d.Width) <= 0 || D(d.Length) <= 0)
            {
                AppDialog.Show(Lang.T("Receipts.Warn.SpecRequired", d.Id, d.WoodType),
                    Lang.T("Common.AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if ((int)D(d.Quantity) <= 0)
            {
                AppDialog.Show(Lang.T("Receipts.Warn.QuantityRequired", d.Id), Lang.T("Common.AppTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // Đơn giá lệch so với báo giá → cảnh báo + hỏi có ghi đè giá mới vào báo giá không.
        foreach (var d in _draftLots) Recalculate(d);
        var mismatched = _draftLots
            .Where(d => d.QuotedPrice > 0 && d.EffectivePrice != d.QuotedPrice)
            .ToList();
        var updateQuotation = false;
        if (mismatched.Count > 0)
        {
            var lines = mismatched.Select(d => new PriceMismatchLine(
                d.Id,
                Fmt.Money(d.QuotedPrice, d.QuotedCurrency ?? SelectedCurrency),
                Fmt.Money(d.EffectivePrice, SelectedCurrency)));
            var dlg = new PriceMismatchDialog(lines) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;     // Không → hủy lưu, quay lại form
            updateQuotation = dlg.UpdateQuotation;
        }

        var receiptId = _mode == "edit"
            ? _editingReceiptId
            : $"REC-{Random.Shared.Next(10000, 99999)}";
        var receipt = new WarehouseReceipt
        {
            Id = receiptId,
            SupplierId = supplierId,
            Date = date,
            Invoice = invoice,
            PackingList = packingList,
            Status = "completed"
        };

        foreach (var d in _draftLots)
        {
            Recalculate(d);
            var quantity = (int)D(d.Quantity);
            var thicknessMm = ParseThickness(d);
            var isFootage = AppState.GetVolumeRule(d.WoodType) == VolumeRule.ByFootage;
            // Gỗ footage: giữ nguyên ký hiệu inch người dùng nhập (độ dày + độ dài) để hiển thị lại.
            var thicknessNote = isFootage ? Blank2Null(d.Thickness) : null;
            var lengthNote = isFootage ? Blank2Null(d.LengthNote) : null;
            var thicknessLabel = thicknessNote ?? $"{Fmt.Num(thicknessMm)}mm";
            var subPart = string.IsNullOrWhiteSpace(d.WoodSubType) ? "" : $" ({d.WoodSubType})";
            receipt.Lots.Add(new WoodLot
            {
                Id = d.Id.Trim().ToUpperInvariant(),
                SupplierId = supplierId,
                ImportDate = date,
                ReceiptId = receiptId,
                Invoice = invoice,
                PackingList = packingList,
                DeliveryNote = Blank2Null(d.DeliveryNote),
                ForestList = Blank2Null(forestList),
                WoodType = d.WoodType,
                WoodSubType = d.WoodSubType,
                WoodName = string.Join(" ", new[] { d.WoodType + subPart, Blank2Null(d.Origin), thicknessLabel }
                    .Where(x => !string.IsNullOrWhiteSpace(x))),
                ThicknessMm = thicknessMm,
                ThicknessNote = thicknessNote,
                WidthMm = D(d.Width),
                LengthMm = D(d.Length),
                LengthNote = lengthNote,
                OriginalQuantity = quantity,
                Quantity = quantity,
                Footage = D(d.Footage),
                Cbm = d.Cbm,
                RemainingCbm = d.Cbm,
                VolumeDecimals = d.VolumeDecimals,
                VolumeAdjustment = d.VolumeAdjustment,
                Price = d.EffectivePrice,
                PriceCurrency = SelectedCurrency,
                ExchangeRate = (decimal)SelectedExchangeRate,
                TaxPercent = (decimal)SelectedTaxPercent,
                CostPriceVnd = d.CostPriceVnd,
                TotalValueVnd = d.TotalValueVnd,
                Origin = Blank2Null(d.Origin),
                Grade = Blank2Null(d.Grade)
            });
        }

        try
        {
            if (_mode == "edit") AppState.UpdateReceipt(receipt);
            else AppState.AddReceipt(receipt);
            if (updateQuotation) ApplyQuotationPriceUpdates(mismatched);
            var saved = AppState.Receipts.FirstOrDefault(r => r.Id == receiptId);
            if (saved != null) EnterViewMode(saved);
            else { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
        }
        catch (Exception ex)
        {
            AppDialog.Show(ex.Message, Lang.T("Common.CannotSaveTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Ghi đè đơn giá vừa nhập vào đúng dòng báo giá đã khớp (kèm "Chỉnh sửa lần cuối" do
    /// <c>AppState.UpdateQuotationItem</c> tự set). Nhiều kiện cùng khớp 1 dòng giá → lấy kiện CUỐI.
    /// </summary>
    private void ApplyQuotationPriceUpdates(List<DraftLot> mismatched)
    {
        var currency = SelectedCurrency;
        // Gom trước: mỗi lần UpdateQuotationItem sẽ Reload AppState nên không iterate list cũ được.
        var updates = mismatched
            .Where(d => !string.IsNullOrEmpty(d.QuotedItemId))
            .GroupBy(d => d.QuotedItemId)
            .Select(g => (ItemId: g.Key, Price: g.Last().EffectivePrice))
            .ToList();

        foreach (var (itemId, price) in updates)
        {
            var item = AppState.Quotations.SelectMany(q => q.Items).FirstOrDefault(i => i.Id == itemId);
            if (item == null) continue;
            item.Price = price;
            item.PriceCurrency = currency;   // giá vừa nhập theo đơn vị tiền tệ của phiếu nhập
            try
            {
                AppState.UpdateQuotationItem(item);
            }
            catch (Exception ex)
            {
                AppDialog.Show(ex.Message, Lang.T("Common.CannotSaveTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    // ---------------- Lịch sử (DataGrid + tìm kiếm / lọc) ----------------

    public sealed class RecRow
    {
        public WarehouseReceipt Receipt { get; }
        public string SupplierId => Receipt.SupplierId;
        public string Id => Receipt.Id;
        public string SupplierName { get; }
        public DateTime Date => Receipt.Date;
        public string DateText => Fmt.Date(Receipt.Date);
        public string Invoice => Receipt.Invoice ?? "";
        public string InvoiceText => string.IsNullOrWhiteSpace(Receipt.Invoice) ? "—" : Receipt.Invoice;
        public int LotCount => Receipt.Lots.Count;
        public string LotCountText => Lang.T("Receipts.RecRow.LotCountText", Receipt.Lots.Count);
        public double Vol { get; }
        public string VolText => $"{Fmt.M3(Vol)} m³";
        public decimal VndTotal { get; }
        public string VndText => Fmt.Vnd(VndTotal);
        public decimal TaxTotal { get; }
        public string TaxText => Fmt.Vnd(TaxTotal);
        public decimal Val => VndTotal + TaxTotal;
        public string ValText => Fmt.Vnd(Val);
        public RecRow(WarehouseReceipt r)
        {
            Receipt = r;
            SupplierName = AppState.FindSupplier(r.SupplierId)?.Name ?? "Unknown";
            Vol = r.Lots.Sum(l => l.Cbm);
            VndTotal = r.Lots.Sum(LotVnd);
            TaxTotal = r.Lots.Sum(l => WoodVolumeCalculator.CalculateTaxAmountVnd(LotVnd(l), l.TaxPercent));
        }

        /// <summary>
        /// Tiền hàng VND (chưa thuế) của 1 kiện tại thời điểm NHẬP — dùng thể tích BAN ĐẦU (l.Cbm),
        /// không dùng RemainingCbm (đã bị Xuất Kho trừ dần). Phiếu nhập là chứng từ lịch sử, giá trị
        /// của nó không được đổi theo tồn kho hiện tại — khớp đúng với cách LoadReceiptIntoForm/ToDraft
        /// hiển thị lại phiếu (luôn dựng lại từ OriginalQuantity).
        /// </summary>
        private static decimal LotVnd(WoodLot l) =>
            WoodVolumeCalculator.ConvertToVnd(WoodVolumeCalculator.CalculateTotalPrice(l.Price, l.Cbm), l.ExchangeRate);
    }

    private readonly List<RecRow> _recRows = new();
    private ICollectionView _recView;

    private void PopulateSupplierFilter()
    {
        var current = (FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterSupplier.Items.Clear();
        FilterSupplier.Items.Add(new ComboBoxItem { Content = Lang.T("Receipts.Filter.AllSuppliers"), Tag = "ALL" });
        foreach (var s in AppState.Suppliers)
            FilterSupplier.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id });
        foreach (ComboBoxItem it in FilterSupplier.Items)
            if ((it.Tag as string) == current) { FilterSupplier.SelectedItem = it; break; }
        if (FilterSupplier.SelectedItem == null) FilterSupplier.SelectedIndex = 0;
    }

    private void RebuildHistory()
    {
        PopulateSupplierFilter();
        _recRows.Clear();
        foreach (var r in AppState.Receipts) _recRows.Add(new RecRow(r));

        if (_recView == null)
        {
            _recView = CollectionViewSource.GetDefaultView(_recRows);
            _recView.Filter = HistoryFilter;
            // Mặc định sắp xếp theo ngày nhập tăng dần
            _recView.SortDescriptions.Add(new SortDescription(nameof(RecRow.Date), ListSortDirection.Ascending));
            HistoryGrid.ItemsSource = _recView;
            ActionGrid.ItemsSource = _recView;   // cột thao tác tách riêng, cùng nguồn
            ColRecDate.SortDirection = ListSortDirection.Ascending;   // hiện mũi tên sort mặc định trên header
        }
        _recView.Refresh();
        UpdateEmpty();
    }

    private bool HistoryFilter(object o)
    {
        var r = (RecRow)o;
        var sup = (FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        if (sup != "ALL" && r.SupplierId != sup) return false;

        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        var matchSearch = term.Length == 0
            || r.Id.ToLowerInvariant().Contains(term)
            || (r.Receipt.Invoice ?? "").ToLowerInvariant().Contains(term)
            || (r.Receipt.PackingList ?? "").ToLowerInvariant().Contains(term)
            || r.SupplierName.ToLowerInvariant().Contains(term);
        if (!matchSearch) return false;

        bool Contains(string cellText, string filterBox) =>
            string.IsNullOrWhiteSpace(filterBox) ||
            (cellText ?? "").ToLowerInvariant().Contains(filterBox.Trim().ToLowerInvariant());

        var matchDate = FDateFilter.SelectedDate == null || r.Receipt.Date.Date == FDateFilter.SelectedDate.Value.Date;

        return matchDate &&
            Contains(r.Id, FIdFilter.Text) &&
            Contains(r.InvoiceText, FInvoiceFilter.Text) &&
            Contains(r.LotCountText, FLotCountFilter.Text) &&
            Contains(r.VolText, FVolFilter.Text) &&
            Contains(r.VndText, FVndFilter.Text) &&
            Contains(r.TaxText, FTaxFilter.Text) &&
            Contains(r.ValText, FValFilter.Text);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_recView == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        BtnClearColumnFilters.Visibility = AnyColumnFilterActive() ? Visibility.Visible : Visibility.Collapsed;
        _recView.Refresh();
        UpdateEmpty();
    }

    // ---------------- Bộ lọc theo từng cột ----------------

    private bool AnyColumnFilterActive() =>
        !string.IsNullOrWhiteSpace(FIdFilter.Text) || !string.IsNullOrWhiteSpace(FInvoiceFilter.Text) ||
        !string.IsNullOrWhiteSpace(FLotCountFilter.Text) || !string.IsNullOrWhiteSpace(FVolFilter.Text) ||
        !string.IsNullOrWhiteSpace(FVndFilter.Text) || !string.IsNullOrWhiteSpace(FTaxFilter.Text) ||
        !string.IsNullOrWhiteSpace(FValFilter.Text) || FDateFilter.SelectedDate != null ||
        ((FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL") != "ALL";

    private void BtnToggleColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        var expand = ColumnFilterPanel.Visibility != Visibility.Visible;
        ColumnFilterPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        ToggleColumnFiltersLabel.Text = expand ? Lang.T("Common.HideColumnFilter") : Lang.T("Common.FilterByColumn");
    }

    private void BtnClearColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in new[] { FIdFilter, FInvoiceFilter, FLotCountFilter, FVolFilter, FVndFilter, FTaxFilter, FValFilter })
            box.Text = "";
        FDateFilter.SelectedDate = null;
        FilterSupplier.SelectedIndex = 0;
        BtnClearColumnFilters.Visibility = Visibility.Collapsed;
        _recView.Refresh();
        UpdateEmpty();
    }

    private void UpdateEmpty()
    {
        EmptyRow.Visibility = _recView.Cast<object>().Any() ? Visibility.Collapsed : Visibility.Visible;
    }
}

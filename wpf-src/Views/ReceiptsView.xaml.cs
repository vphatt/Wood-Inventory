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
        public string Origin = "";   // Xuất xứ (thay cho Grade cũ)
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
        var vnd = SelectedCurrency == "VND";
        FExchangeRate.IsReadOnly = vnd;
        FExchangeRate.Background = vnd ? (Brush)FindResource("Slate50") : Brushes.White;
        FExchangeRate.Text = vnd ? "1" : Fmt.Num((double)AppState.Settings.DefaultExchangeRate);
        if (!IsLoaded) return;
        foreach (var update in _rowUpdaters.ToList()) update();
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
        else foreach (var update in _rowUpdaters.ToList()) update();
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
        double thickness, double width, double length, string origin)
    {
        if (string.IsNullOrEmpty(supplierId)) return null;
        var quotation = AppState.Quotations.FirstOrDefault(q => q.SupplierId == supplierId);
        if (quotation == null) return null;
        return QuotationPriceMatcher.FindBestMatch(quotation.Items, woodType,
            thickness: thickness, width: width, length: length, origin: origin, woodSubType: woodSubType);
    }

    private void Recalculate(DraftLot lot)
    {
        var thickness = ParseThickness(lot);
        var matched = LookupQuotationItem(SelectedSupplierId, lot.WoodType, lot.WoodSubType, thickness, D(lot.Width), D(lot.Length), lot.Origin);
        lot.PriceFromQuotation = matched != null && matched.Price > 0;
        lot.QuotedPrice = lot.PriceFromQuotation ? matched.Price : 0;
        lot.QuotedCurrency = lot.PriceFromQuotation ? matched.PriceCurrency : null;
        lot.QuotedItemId = lot.PriceFromQuotation ? matched.Id : null;
        // Đơn giản hóa: không quan tâm báo giá NCC ghi theo USD hay VND, chỉ lấy con số — đơn vị tiền tệ
        // do người dùng chọn ở header phiếu (FCurrency) quyết định, Tỷ giá luôn nhân như cũ (VND thì Tỷ giá đã bị khóa = 1).
        // Đơn giá LUÔN lấy con số đang có trong ô: giá báo giá chỉ được TỰ ĐIỀN vào ô (xem Update() trong BuildLotRow),
        // sau đó user có quyền sửa đè — lệch so với báo giá thì hiện icon ≠ và cảnh báo lúc lưu.
        lot.EffectivePrice = (decimal)D(lot.ManualPriceText);
        lot.Cbm = WoodVolumeCalculator.CalculateVolume(lot.WoodType, thickness, D(lot.Width),
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
        for (var i = 0; i < _draftLots.Count; i++)
        {
            var lot = _draftLots[i];
            LotRowsPanel.Items.Add(_mode == "view" ? BuildLotRowReadOnly(lot, i + 1) : BuildLotRow(lot, i + 1));
        }
        UpdateTotals();
    }

    /// <summary>Chế độ xem: danh sách kiện hiển thị thuần bảng đọc (TextBlock), không phải field form.</summary>
    private FrameworkElement BuildLotRowReadOnly(DraftLot lot, int stt)
    {
        Recalculate(lot);
        var isFootage = AppState.GetVolumeRule(lot.WoodType) == VolumeRule.ByFootage;

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        foreach (var w in new[] { 45.0, 100, 120, 120, 120, 95, 95, 95, 95, 95, 70, 90, 70, 100, 140, 110, 120, 110, -1, 50 })
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
        Cell(lot.Thickness, 6, HorizontalAlignment.Center);
        Cell(isFootage ? "-" : lot.Width, 7, HorizontalAlignment.Center);
        Cell(isFootage ? lot.LengthNote : lot.Length, 8, HorizontalAlignment.Center);
        Cell(isFootage ? lot.Footage : "-", 9, HorizontalAlignment.Center);
        Cell(lot.Quantity, 10, HorizontalAlignment.Center);
        Cell(Fmt.M3(lot.Cbm, lot.VolumeDecimals), 11, HorizontalAlignment.Right);
        Cell(lot.VolumeDecimals.ToString(), 12, HorizontalAlignment.Center, color: (Brush)FindResource("Slate400"));
        Cell(lot.VolumeAdjustment == 0 ? "-" : (lot.VolumeAdjustment > 0 ? "+" : "") + Fmt.M3(lot.VolumeAdjustment, lot.VolumeDecimals),
            13, HorizontalAlignment.Right, color: (Brush)FindResource(lot.VolumeAdjustment == 0 ? "Slate400" : "Amber600"));
        Cell(lot.EffectivePrice > 0 ? Fmt.Money(lot.EffectivePrice, SelectedCurrency) : Lang.T("Receipts.NoPriceMatch"), 14, HorizontalAlignment.Right,
            color: (Brush)FindResource(lot.EffectivePrice > 0 ? "Slate800" : "Slate400"));
        Cell(Fmt.Money(lot.TotalPrice, SelectedCurrency), 15, HorizontalAlignment.Right);
        Cell(Fmt.Vnd(lot.TotalVnd), 16, HorizontalAlignment.Right);
        Cell(Fmt.Vnd(lot.TaxVnd), 17, HorizontalAlignment.Right);
        Cell(Fmt.Vnd(lot.TotalValueVnd), 18, HorizontalAlignment.Right,
            weight: FontWeights.SemiBold, color: (Brush)FindResource("Emerald600"), margin: new Thickness(6, 0, 12, 0));
        // Cột 19 (Xóa): để trống — chế độ xem không cho xóa.

        return new Border
        {
            BorderBrush = (Brush)FindResource("Slate100"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.White,
            Child = grid
        };
    }

    private FrameworkElement BuildLotRow(DraftLot lot, int stt)
    {
        Recalculate(lot);

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        // STT, MãKiện, PhiếuGiaoHàng, LoạiGỗ, PhânLoại, XuấtXứ, Dày, Rộng, Dài, Footage, SốLượng, ThểTích, SốTP, ĐiềuChỉnh,
        // ĐơnGiáUSD, TổngTiềnUSD, TổngTiềnVND, TiềnThuếVND, TổngCộngVND(*), Xóa
        foreach (var w in new[] { 45.0, 100, 120, 120, 120, 95, 95, 95, 95, 95, 70, 90, 70, 100, 140, 110, 120, 110, -1, 50 })
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
                if (untouched && lot.QuotedPrice > 0)
                {
                    var auto = Fmt.Num((double)lot.QuotedPrice);
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

        // Gợi ý (tối đa 3) lấy từ báo giá NCC khớp loại gỗ cha + phân loại con của kiện. Dòng giá có khai
        // danh sách giá trị rời rạc (vd "1220/2440/3000") thì TÁCH ra thành từng giá trị riêng để gợi ý,
        // không phải Min/Max range — xem QuotationItem.WidthValues/LengthValues/ThicknessValues. Dòng giá là
        // KHOẢNG thật (Từ khác Đến) thì hiện hint dạng "20-25" (không phải số cụ thể) — người dùng tự chọn
        // 1 con số hợp lệ nằm trong khoảng đó để gõ vào, không kỳ vọng gõ nguyên văn "20-25".
        bool Footage() => AppState.GetVolumeRule(lot.WoodType) == VolumeRule.ByFootage;
        List<string> Suggest(Func<QuotationItem, IEnumerable<string>> pick) => DistinctSuggest(MatchingQuotationItems(lot).SelectMany(pick));
        string RangeHint(double? min, double? max) =>
            min.HasValue && max.HasValue && Math.Abs(min.Value - max.Value) > 0.0001
                ? $"{Fmt.Num(min.Value)}-{Fmt.Num(max.Value)}"
                : Fmt.Num((min ?? max ?? 0));
        List<string> OriginSuggest() => Suggest(i => new[] { i.Origin });
        List<string> ThickSuggest() => Suggest(i => Footage()
            ? new[] { i.ThicknessMinNote }
            : !string.IsNullOrWhiteSpace(i.ThicknessValues)
                ? Fmt.ParseValueList(i.ThicknessValues).Select(Fmt.Num)
                : new[] { i.ThicknessMin.HasValue || i.ThicknessMax.HasValue ? RangeHint(i.ThicknessMin, i.ThicknessMax) : null });
        List<string> WidthSuggest() => Suggest(i => !string.IsNullOrWhiteSpace(i.WidthValues)
            ? Fmt.ParseValueList(i.WidthValues).Select(Fmt.Num)
            : new[] { i.WidthMin.HasValue || i.WidthMax.HasValue ? RangeHint(i.WidthMin, i.WidthMax) : null });
        List<string> LengthSuggest() => Suggest(i => !string.IsNullOrWhiteSpace(i.LengthValues)
            ? Fmt.ParseValueList(i.LengthValues).Select(Fmt.Num)
            : new[] { i.LengthMin.HasValue || i.LengthMax.HasValue ? RangeHint(i.LengthMin, i.LengthMax) : null });

        // Các ô kích thước (tạo trước để ẩn/hiện theo nguyên tắc tính m³) — Rộng/Dài có dropdown gợi ý.
        var widthBox = BuildSuggestCell(lot.Width, s => { lot.Width = s; Update(); }, WidthSuggest, center: true);
        var lengthBox = BuildSuggestCell(lot.Length, s => { lot.Length = s; Update(); }, LengthSuggest, center: true);
        var lengthNoteBox = Cell(lot.LengthNote ?? "", s => { lot.LengthNote = s; }, mono: true, center: true);
        lengthNoteBox.Margin = new Thickness(6, 0, 6, 0);
        lengthNoteBox.ToolTip = Lang.T("Receipts.LengthNoteTooltip");
        var footageBox = Cell(lot.Footage, s => { lot.Footage = s; Update(); }, mono: true, center: true);
        footageBox.Margin = new Thickness(6, 0, 6, 0);

        // Quy cách → ẩn Footage (hiện Rộng); Footage → ẩn Rộng (hiện Footage). Cột Dài: quy cách=mm, footage=inch.
        void ApplyRuleVisibility()
        {
            var footage = AppState.GetVolumeRule(lot.WoodType) == VolumeRule.ByFootage;
            widthBox.Visibility = footage ? Visibility.Collapsed : Visibility.Visible;
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

        void RebuildSub()
        {
            subCombo.Items.Clear();
            subCombo.Items.Add(new ComboBoxItem { Content = Lang.T("Receipts.SubTypePlaceholder"), Tag = "" });
            foreach (var s in AppState.SubNamesOf(lot.WoodType))
                subCombo.Items.Add(new ComboBoxItem { Content = s, Tag = s, IsSelected = s == lot.WoodSubType });
            if (subCombo.SelectedIndex < 0) subCombo.SelectedIndex = 0;
        }
        subCombo.SelectionChanged += (_, _) =>
        {
            var v = (subCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            lot.WoodSubType = string.IsNullOrEmpty(v) ? null : v;
            Update();   // đổi phân loại con có thể đổi giá báo giá khớp được
        };

        typeCombo.Items.Add(new ComboBoxItem { Content = Lang.T("Receipts.WoodTypePlaceholder"), Tag = "", IsSelected = string.IsNullOrEmpty(lot.WoodType) });
        foreach (var t in AppState.CategoryNames)
            typeCombo.Items.Add(new ComboBoxItem { Content = t, Tag = t, IsSelected = t == lot.WoodType });
        if (typeCombo.SelectedIndex < 0) typeCombo.SelectedIndex = 0;
        typeCombo.SelectionChanged += (_, _) =>
        {
            lot.WoodType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            lot.WoodSubType = null;      // đổi loại cha → reset phân loại con
            RebuildSub();
            ApplyRuleVisibility();
            Update();
        };
        RebuildSub();
        ApplyRuleVisibility();

        Grid.SetColumn(typeCombo, 3); grid.Children.Add(typeCombo);
        Grid.SetColumn(subCombo, 4); grid.Children.Add(subCombo);

        // 5. Xuất xứ — ô nhập kèm dropdown gợi ý (tối đa 3) theo xuất xứ trong báo giá NCC khớp loại/phân loại con
        var originCell = BuildSuggestCell(lot.Origin, s => { lot.Origin = s; Update(); }, OriginSuggest, center: true);
        Grid.SetColumn(originCell, 5); grid.Children.Add(originCell);

        // 6. Dày (gỗ footage chấp nhận ký hiệu inch: 1", 4/4", 5/4"...) — có dropdown gợi ý theo báo giá
        var thickBox = BuildSuggestCell(lot.Thickness, s => { lot.Thickness = s; Update(); }, ThickSuggest, center: true,
            tooltip: Lang.T("Receipts.ThicknessTooltip"));
        Grid.SetColumn(thickBox, 6); grid.Children.Add(thickBox);

        // 7. Rộng (ẩn nếu gỗ footage)
        Grid.SetColumn(widthBox, 7); grid.Children.Add(widthBox);
        // 8. Dài (quy cách = mm; footage = ký hiệu inch — hai ô chồng nhau, toggle theo rule)
        Grid.SetColumn(lengthBox, 8); grid.Children.Add(lengthBox);
        Grid.SetColumn(lengthNoteBox, 8); grid.Children.Add(lengthNoteBox);
        // 9. Footage (ẩn nếu gỗ quy cách)
        Grid.SetColumn(footageBox, 9); grid.Children.Add(footageBox);

        // 10. Số lượng
        var qtyBox = Cell(lot.Quantity, s => { lot.Quantity = s; Update(); }, mono: true, center: true);
        qtyBox.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(qtyBox, 10); grid.Children.Add(qtyBox);

        // 11. Thể tích
        Grid.SetColumn(cbmText, 11); grid.Children.Add(cbmText);

        // 12. Số thập phân làm tròn m³ riêng dòng (mặc định 5)
        var decimalsBox = Cell(lot.VolumeDecimals.ToString(), s =>
        {
            lot.VolumeDecimals = Math.Clamp((int)D(s), 0, 15);
            Update();
        }, mono: true, center: true);
        decimalsBox.ToolTip = Lang.T("Receipts.Row.DecimalsTooltip");
        Grid.SetColumn(decimalsBox, 12); grid.Children.Add(decimalsBox);

        // 13. Điều chỉnh tay +/- cộng vào m³ sau khi làm tròn
        var adjustmentBox = Cell(lot.VolumeAdjustment == 0 ? "" : Fmt.Num(lot.VolumeAdjustment), s =>
        {
            lot.VolumeAdjustment = D(s);
            Update();
        }, mono: true);
        adjustmentBox.TextAlignment = TextAlignment.Right;
        adjustmentBox.ToolTip = Lang.T("Receipts.Row.AdjustmentTooltip");
        Grid.SetColumn(adjustmentBox, 13); grid.Children.Add(adjustmentBox);

        // 14. Đơn giá  15-18. Tổng tiền gốc/VND, Tiền thuế, Tổng cộng
        Grid.SetColumn(priceBox, 14); grid.Children.Add(priceBox);
        Grid.SetColumn(priceHint, 14); grid.Children.Add(priceHint);
        Grid.SetColumn(mismatchIcon, 14); grid.Children.Add(mismatchIcon);   // đè mép trái ô đơn giá
        Grid.SetColumn(totalUsdText, 15); grid.Children.Add(totalUsdText);
        Grid.SetColumn(totalVndText, 16); grid.Children.Add(totalVndText);
        Grid.SetColumn(taxVndText, 17); grid.Children.Add(taxVndText);
        Grid.SetColumn(grandTotalText, 18); grid.Children.Add(grandTotalText);

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
        Grid.SetColumn(del, 19); grid.Children.Add(del);

        return new Border
        {
            BorderBrush = (Brush)FindResource("Slate100"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.White,
            Child = grid
        };
    }

    private readonly List<Action> _rowUpdaters = new();

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
        box.TextChanged += (_, _) => onChange(box.Text);
        return box;
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

    /// <summary>Chuẩn hóa danh sách gợi ý: bỏ rỗng, bỏ trùng (không phân biệt hoa thường), sắp xếp.</summary>
    private static List<string> DistinctSuggest(IEnumerable<string> values) =>
        values.Select(v => (v ?? "").Trim()).Where(v => v.Length > 0)
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// Ô nhập kèm dropdown gợi ý (tối đa 3) + cho phép gõ để lọc; TỰ BUNG khi focus (kể cả chưa gõ).
    /// Trả về Grid bọc TextBox + Popup (Popup nổi, không ảnh hưởng layout ô).
    /// </summary>
    private FrameworkElement BuildSuggestCell(string initial, Action<string> onChange, Func<List<string>> suggest,
        bool center = false, string tooltip = null)
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

        var list = new ListBox { BorderThickness = new Thickness(0), MaxHeight = 132, FontSize = 12 };
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

        void Refresh()
        {
            if (ReadOnly) { popup.IsOpen = false; return; }
            var typed = (box.Text ?? "").Trim();
            var sugg = suggest()
                .Where(o => typed.Length == 0 || o.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(3).ToList();
            // Không mở nếu không có gợi ý, hoặc gợi ý duy nhất trùng khít text đang gõ.
            if (sugg.Count == 0 || (sugg.Count == 1 && string.Equals(sugg[0], typed, StringComparison.OrdinalIgnoreCase)))
            {
                popup.IsOpen = false;
                return;
            }
            list.ItemsSource = sugg;
            popup.IsOpen = box.IsKeyboardFocused || box.IsKeyboardFocusWithin;
        }

        void Pick(string s)
        {
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
        box.TextChanged += (_, _) => { onChange(box.Text); if (!suppress) Refresh(); };
        box.LostKeyboardFocus += (_, _) => { if (!list.IsKeyboardFocusWithin) popup.IsOpen = false; };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && popup.IsOpen && list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
                (list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && popup.IsOpen) { popup.IsOpen = false; e.Handled = true; }
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
        return host;
    }

    private void UpdateTotals()
    {
        foreach (var lot in _draftLots) Recalculate(lot);
        TotalCbm.Text = $"{Fmt.M3(_draftLots.Sum(l => l.Cbm))} m³";
        TotalValue.Text = Fmt.Vnd(_draftLots.Sum(l => l.TotalValueVnd));
    }

    private void FSupplier_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
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
        // Đang mở sẵn ở chế độ thêm mới → bấm lần nữa thì đóng
        if (AddFormPanel.Visibility == Visibility.Visible && _mode == "add")
        {
            AddFormPanel.Visibility = Visibility.Collapsed;
            return;
        }
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
        FSupplier.IsEnabled = !ro;
        FTaxPercent.IsEnabled = !ro;
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
        FCurrency.SelectedIndex = 0;   // mặc định USD
        FExchangeRate.IsReadOnly = false;
        FExchangeRate.Background = Brushes.White;
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
            Width = Fmt.Num(l.WidthMm),
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
        if ((sender as FrameworkElement)?.DataContext is RecRow r) EnterViewMode(r.Receipt);
    }

    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecRow r) EnterEditMode(r.Receipt);
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
                Origin = Blank2Null(d.Origin)
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

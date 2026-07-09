using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
        public string ManualPriceUsd = "";
        public int VolumeDecimals = AppState.Settings.DefaultVolumeDecimals;   // số chữ số thập phân làm tròn m³ riêng dòng này
        public double VolumeAdjustment;    // điều chỉnh tay +/- cộng vào m³ sau khi làm tròn (mặc định 0)

        // Kết quả tính toán gần nhất
        public double Cbm;
        public decimal EffectivePriceUsd;
        public decimal CostPriceVnd;
        public decimal TotalUsd;
        public decimal TotalVnd;
        public decimal TaxVnd;
        public decimal TotalValueVnd;
        public bool PriceFromQuotation;
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

    /// <summary>Tỷ giá + thuế nhập khẩu áp dụng chung cho cả phiếu — không khai báo riêng theo từng kiện.</summary>
    private double SelectedExchangeRate => D(FExchangeRate.Text);
    private double SelectedTaxPercent => D((FTaxPercent.SelectedItem as ComboBoxItem)?.Tag as string ?? "10");

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
            Main?.SetBreadcrumbDetail("Bảng tổng chi tiết nhập kho", BackToReceipts);
            return;
        }

        var current = (FSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        FSupplier.Items.Clear();
        FSupplier.Items.Add(new ComboBoxItem { Content = "-- Chọn Nhà Cung Cấp --", Tag = "" });
        foreach (var s in AppState.Suppliers)
            FSupplier.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id });
        foreach (ComboBoxItem item in FSupplier.Items)
            if ((item.Tag as string) == current) FSupplier.SelectedItem = item;
        if (FSupplier.SelectedItem == null) FSupplier.SelectedIndex = 0;

        RebuildHistory();
    }

    private static double D(string s) => Fmt.ParseNum(s);

    private static string Blank2Null(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Parse độ dày theo loại gỗ — nhóm Footage chấp nhận thêm ký hiệu inch/quarter (1", 4/4").</summary>
    private static double ParseThickness(DraftLot lot) =>
        AppState.GetVolumeRule(lot.WoodType) == VolumeRule.ByFootage
            ? WoodVolumeCalculator.ParseFootageThicknessMm(lot.Thickness)
            : D(lot.Thickness);

    private string SelectedSupplierId => (FSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>Tra đơn giá USD từ báo giá của NCC — khớp dòng giá cụ thể nhất theo loại gỗ + kích thước + grade.</summary>
    private static decimal LookupQuotationPrice(string supplierId, string woodType, string woodSubType,
        double thickness, double width, double length, string origin)
    {
        if (string.IsNullOrEmpty(supplierId)) return 0;
        var quotation = AppState.Quotations.FirstOrDefault(q => q.SupplierId == supplierId);
        if (quotation == null) return 0;
        var item = QuotationPriceMatcher.FindBestMatch(quotation.Items, woodType,
            thickness: thickness, width: width, length: length, origin: origin, woodSubType: woodSubType);
        return item?.PriceUsd ?? 0;
    }

    private void Recalculate(DraftLot lot)
    {
        var thickness = ParseThickness(lot);
        var quotPrice = LookupQuotationPrice(SelectedSupplierId, lot.WoodType, lot.WoodSubType, thickness, D(lot.Width), D(lot.Length), lot.Origin);
        lot.PriceFromQuotation = quotPrice > 0;
        lot.EffectivePriceUsd = quotPrice > 0 ? quotPrice : (decimal)D(lot.ManualPriceUsd);
        lot.Cbm = WoodVolumeCalculator.CalculateVolume(lot.WoodType, thickness, D(lot.Width),
            D(lot.Length), (int)D(lot.Quantity), D(lot.Footage), lot.VolumeDecimals, lot.VolumeAdjustment);
        lot.CostPriceVnd = WoodVolumeCalculator.CalculateCostPricePerM3(lot.EffectivePriceUsd,
            (decimal)SelectedExchangeRate, (decimal)SelectedTaxPercent);
        lot.TotalUsd = WoodVolumeCalculator.CalculateTotalUsd(lot.EffectivePriceUsd, lot.Cbm);
        lot.TotalVnd = WoodVolumeCalculator.ConvertUsdToVnd(lot.TotalUsd, (decimal)SelectedExchangeRate);
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
        Cell(lot.EffectivePriceUsd > 0 ? Fmt.Usd(lot.EffectivePriceUsd) : "Chưa xác định", 14, HorizontalAlignment.Right,
            color: (Brush)FindResource(lot.EffectivePriceUsd > 0 ? "Slate800" : "Slate400"));
        Cell(Fmt.Usd(lot.TotalUsd), 15, HorizontalAlignment.Right);
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
        totalUsdText.Text = Fmt.Usd(lot.TotalUsd);
        totalVndText.Text = Fmt.Vnd(lot.TotalVnd);
        taxVndText.Text = Fmt.Vnd(lot.TaxVnd);
        grandTotalText.Text = Fmt.Vnd(lot.TotalValueVnd);
        var priceBox = new TextBox
        {
            Style = (Style)FindResource("CellInputMono"),
            Text = lot.PriceFromQuotation ? Fmt.Num((double)lot.EffectivePriceUsd) : lot.ManualPriceUsd,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            IsReadOnly = ReadOnly || lot.PriceFromQuotation,
            Foreground = lot.PriceFromQuotation
                ? (Brush)FindResource("Emerald600")
                : (Brush)FindResource("Slate700"),
            FontWeight = lot.PriceFromQuotation ? FontWeights.SemiBold : FontWeights.Normal
        };
        // Chưa khớp báo giá và cũng chưa nhập tay → hiện "Chưa xác định" đè lên ô trống
        // (giống pattern SearchHint), thay vì chỉ tô đỏ giá trị hiện có.
        var priceHint = new TextBlock
        {
            Text = "Chưa xác định", FontStyle = FontStyles.Italic,
            FontFamily = (FontFamily)FindResource("FontMono"), FontSize = 11,
            Foreground = (Brush)FindResource("Slate400"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 12, 0), IsHitTestVisible = false,
            Visibility = !lot.PriceFromQuotation && string.IsNullOrWhiteSpace(lot.ManualPriceUsd)
                ? Visibility.Visible : Visibility.Collapsed
        };

        void Update()
        {
            Recalculate(lot);
            cbmText.Text = Fmt.M3(lot.Cbm, lot.VolumeDecimals);
            totalUsdText.Text = Fmt.Usd(lot.TotalUsd);
            totalVndText.Text = Fmt.Vnd(lot.TotalVnd);
            taxVndText.Text = Fmt.Vnd(lot.TaxVnd);
            grandTotalText.Text = Fmt.Vnd(lot.TotalValueVnd);
            if (lot.PriceFromQuotation)
            {
                priceBox.IsReadOnly = true;
                priceBox.Foreground = (Brush)FindResource("Emerald600");
                priceBox.FontWeight = FontWeights.SemiBold;
                var autoText = Fmt.Num((double)lot.EffectivePriceUsd);
                if (priceBox.Text != autoText) priceBox.Text = autoText;
                priceHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                priceBox.IsReadOnly = ReadOnly;
                priceBox.Foreground = (Brush)FindResource("Slate700");
                priceBox.FontWeight = FontWeights.Normal;
                // Reset lại text nếu trước đó đang hiện giá tự động (vd đổi độ dày làm mất khớp báo giá)
                if (priceBox.Text != lot.ManualPriceUsd) priceBox.Text = lot.ManualPriceUsd;
                priceHint.Visibility = string.IsNullOrWhiteSpace(lot.ManualPriceUsd)
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateTotals();
        }
        _rowUpdaters.Add(Update);

        priceBox.TextChanged += (_, _) =>
        {
            if (!lot.PriceFromQuotation) { lot.ManualPriceUsd = priceBox.Text; Update(); }
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

        // Các ô kích thước (tạo trước để ẩn/hiện theo nguyên tắc tính m³)
        var widthBox = Cell(lot.Width, s => { lot.Width = s; Update(); }, mono: true, center: true);
        widthBox.Margin = new Thickness(6, 0, 6, 0);
        var lengthBox = Cell(lot.Length, s => { lot.Length = s; Update(); }, mono: true, center: true);
        lengthBox.Margin = new Thickness(6, 0, 6, 0);
        var lengthNoteBox = Cell(lot.LengthNote ?? "", s => { lot.LengthNote = s; }, mono: true, center: true);
        lengthNoteBox.Margin = new Thickness(6, 0, 6, 0);
        lengthNoteBox.ToolTip = "Độ dài dạng inch, vd 96\"108\"120\" (chỉ mô tả, không tính m³)";
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
            subCombo.Items.Add(new ComboBoxItem { Content = "— Không phân loại —", Tag = "" });
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

        typeCombo.Items.Add(new ComboBoxItem { Content = "— Chọn loại gỗ —", Tag = "", IsSelected = string.IsNullOrEmpty(lot.WoodType) });
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

        // 5. Xuất xứ
        var originBox = Cell(lot.Origin, s => { lot.Origin = s; Update(); }, mono: true, center: true);
        originBox.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(originBox, 5); grid.Children.Add(originBox);

        // 6. Dày (gỗ footage chấp nhận ký hiệu inch: 1", 4/4", 5/4"...)
        var thickBox = Cell(lot.Thickness, s => { lot.Thickness = s; Update(); }, mono: true, center: true);
        thickBox.Margin = new Thickness(6, 0, 6, 0);
        thickBox.ToolTip = "Số mm; gỗ nhóm Footage có thể nhập dạng inch: 1\", 4/4\", 5/4\"";
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
        decimalsBox.ToolTip = "Số chữ số thập phân làm tròn m³ của riêng dòng này (mặc định 5)";
        Grid.SetColumn(decimalsBox, 12); grid.Children.Add(decimalsBox);

        // 13. Điều chỉnh tay +/- cộng vào m³ sau khi làm tròn
        var adjustmentBox = Cell(lot.VolumeAdjustment == 0 ? "" : Fmt.Num(lot.VolumeAdjustment), s =>
        {
            lot.VolumeAdjustment = D(s);
            Update();
        }, mono: true);
        adjustmentBox.TextAlignment = TextAlignment.Right;
        adjustmentBox.ToolTip = "Cộng/trừ thêm vào m³ sau khi làm tròn, vd 0,0001 hoặc -0,0002";
        Grid.SetColumn(adjustmentBox, 13); grid.Children.Add(adjustmentBox);

        // 14. Đơn giá USD  15-18. Tổng tiền USD/VND, Tiền thuế, Tổng cộng
        Grid.SetColumn(priceBox, 14); grid.Children.Add(priceBox);
        Grid.SetColumn(priceHint, 14); grid.Children.Add(priceHint);
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
        Main?.SetBreadcrumbDetail("Bảng tổng chi tiết nhập kho", BackToReceipts);
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
            if (!ConfirmDiscard("Những thay đổi sẽ không được lưu, tiếp tục huỷ?")) return;
            var r = AppState.Receipts.FirstOrDefault(x => x.Id == _editingReceiptId);
            if (r != null) { EnterViewMode(r); return; }
        }
        // Đang thêm mới → xác nhận trước khi bỏ thông tin đã nhập
        else if (_mode == "add")
        {
            if (!ConfirmDiscard("Các thông tin chưa được lưu, tiếp tục huỷ?")) return;
        }
        AddFormPanel.Visibility = Visibility.Collapsed;
        EnterAddMode();
    }

    /// <summary>Hộp thoại xác nhận hủy (thông điệp tùy chế độ add/edit).</summary>
    private static bool ConfirmDiscard(string message) =>
        MessageBox.Show(message, "Xác nhận hủy",
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
        FormTitle.Text = "Lập Phiếu Nhập Kho Mới";
        FormSaveBtn.Content = "Lưu phiếu nhập kho";
        FormCancelBtn.Content = "Hủy bỏ";
        FInvoice.Text = "";
        FPackingList.Text = "";
        FForestList.Text = "";
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
        FormTitle.Text = $"Chi Tiết Phiếu Nhập — {r.Id}";
        FormSaveBtn.Content = "Chỉnh sửa";
        FormCancelBtn.Content = "Đóng";
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
        FormTitle.Text = $"Sửa Phiếu Nhập — {_editingReceiptId}";
        FormSaveBtn.Content = "Cập nhật";
        FormCancelBtn.Content = "Hủy sửa";
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
            FExchangeRate.Text = Fmt.Num((double)first.ExchangeRate);
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
            ManualPriceUsd = Fmt.Num((double)l.PriceUsd),
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
        if (MessageBox.Show(
                $"Xóa phiếu nhập {r.Id} cùng {r.Receipt.Lots.Count} kiện gỗ kèm theo?\nHành động này không thể hoàn tác.",
                "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            AppState.DeleteReceipt(r.Id);
            if (_editingReceiptId == r.Id) { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("Vui lòng nhập đầy đủ thông tin chứng từ.", "Quản Lý Gỗ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (SelectedExchangeRate <= 0)
        {
            MessageBox.Show("Tỷ giá VND/USD phải lớn hơn 0.", "Quản Lý Gỗ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_draftLots.Count == 0)
        {
            MessageBox.Show("Phiếu nhập kho phải chứa ít nhất một kiện gỗ.", "Quản Lý Gỗ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var date = FDate.SelectedDate ?? DateTime.Today;

        var ids = _draftLots.Select(l => (l.Id ?? "").Trim().ToUpperInvariant()).ToList();
        var duplicates = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            MessageBox.Show(
                $"Mã kiện gỗ bị trùng lặp trong phiếu: {string.Join(", ", duplicates)}. " +
                "Mỗi kiện gỗ phải có một mã định danh duy nhất.",
                "Quản Lý Gỗ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var d in _draftLots)
        {
            if (string.IsNullOrWhiteSpace(d.Id))
            {
                MessageBox.Show("Vui lòng nhập Mã kiện cho tất cả các dòng kiện gỗ.", "Quản Lý Gỗ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(d.WoodType))
            {
                MessageBox.Show($"Kiện {d.Id}: vui lòng chọn loại gỗ.", "Quản Lý Gỗ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (AppState.CategoryHasSubs(d.WoodType) && string.IsNullOrWhiteSpace(d.WoodSubType))
            {
                MessageBox.Show($"Kiện {d.Id}: {d.WoodType} có phân loại con — vui lòng chọn phân loại con.",
                    "Quản Lý Gỗ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (ParseThickness(d) <= 0)
            {
                MessageBox.Show($"Kiện {d.Id}: Độ dày phải lớn hơn 0.", "Quản Lý Gỗ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (AppState.GetVolumeRule(d.WoodType) == VolumeRule.ByFootage)
            {
                if (D(d.Footage) <= 0)
                {
                    MessageBox.Show($"Kiện {d.Id}: {d.WoodType} tính theo Footage — Footage phải lớn hơn 0.",
                        "Quản Lý Gỗ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (D(d.Width) <= 0 || D(d.Length) <= 0)
            {
                MessageBox.Show($"Kiện {d.Id}: {d.WoodType} tính theo quy cách — Rộng và Dài phải lớn hơn 0.",
                    "Quản Lý Gỗ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if ((int)D(d.Quantity) <= 0)
            {
                MessageBox.Show($"Kiện {d.Id}: Số lượng phải lớn hơn 0.", "Quản Lý Gỗ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
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
                PriceUsd = d.EffectivePriceUsd,
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
            var saved = AppState.Receipts.FirstOrDefault(r => r.Id == receiptId);
            if (saved != null) EnterViewMode(saved);
            else { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------- Lịch sử (DataGrid + tìm kiếm / lọc) ----------------

    public sealed class RecRow
    {
        public WarehouseReceipt Receipt { get; }
        public string SupplierId => Receipt.SupplierId;
        public string Id => Receipt.Id;
        public string SupplierName { get; }
        public string DateText => Fmt.Date(Receipt.Date);
        public string InvoiceText => string.IsNullOrWhiteSpace(Receipt.Invoice) ? "—" : Receipt.Invoice;
        public string LotCountText => $"{Receipt.Lots.Count} kiện";
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
            WoodVolumeCalculator.ConvertUsdToVnd(WoodVolumeCalculator.CalculateTotalUsd(l.PriceUsd, l.Cbm), l.ExchangeRate);
    }

    private readonly List<RecRow> _recRows = new();
    private ICollectionView _recView;

    private void PopulateSupplierFilter()
    {
        var current = (FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterSupplier.Items.Clear();
        FilterSupplier.Items.Add(new ComboBoxItem { Content = "Tất cả nhà cung cấp", Tag = "ALL" });
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
            HistoryGrid.ItemsSource = _recView;
            ActionGrid.ItemsSource = _recView;   // cột thao tác tách riêng, cùng nguồn
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
        if (term.Length == 0) return true;
        return r.Id.ToLowerInvariant().Contains(term)
            || (r.Receipt.Invoice ?? "").ToLowerInvariant().Contains(term)
            || (r.Receipt.PackingList ?? "").ToLowerInvariant().Contains(term)
            || r.SupplierName.ToLowerInvariant().Contains(term);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_recView == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _recView.Refresh();
        UpdateEmpty();
    }

    private void UpdateEmpty()
    {
        EmptyRow.Visibility = _recView.Cast<object>().Any() ? Visibility.Collapsed : Visibility.Visible;
    }
}

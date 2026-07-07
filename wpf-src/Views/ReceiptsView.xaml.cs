using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TimberFlowDesktop.Data;
using TimberFlowDesktop.Domain;
using TimberFlowDesktop.Helpers;

namespace TimberFlowDesktop.Views;

public partial class ReceiptsView : UserControl, IModuleView
{
    private static readonly string[] WoodTypes = { "Gỗ Sồi", "Gỗ Dương", "Gỗ Tần Bì", "Gỗ Thông", "Gỗ Tràm" };

    /// <summary>Một dòng kiện gỗ đang khai báo trong phiếu nhập.</summary>
    private sealed class DraftLot
    {
        public string Id = "LOT-NEW-1";
        public string WoodType = "Gỗ Sồi";
        public string Thickness = "26";
        public string Grade = "FAS";
        public string Width = "150";
        public string Length = "2400";
        public string Footage = "0";
        public string Quantity = "150";
        public string ManualPriceUsd = "0";
        public string ExchangeRate = "25400";
        public string TaxPercent = "10";

        // Kết quả tính toán gần nhất
        public double Cbm;
        public decimal EffectivePriceUsd;
        public decimal CostPriceVnd;
        public decimal TotalValueVnd;
        public bool PriceFromQuotation;
    }

    private readonly List<DraftLot> _draftLots = new();

    public ReceiptsView()
    {
        InitializeComponent();
        FDate.Text = Fmt.Date(DateTime.Today);
        ResetDraft();
        RefreshView();
        Helpers.GridLayoutStore.Attach(HistoryGrid, "receipts");
    }

    private void ResetDraft()
    {
        _draftLots.Clear();
        _draftLots.Add(new DraftLot());
        RebuildLotRows();
    }

    public void RefreshView()
    {
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

    private static double D(string s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private string SelectedSupplierId => (FSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>Tra đơn giá USD từ báo giá đang kích hoạt của NCC (khớp loại gỗ + độ dày + grade).</summary>
    private static decimal LookupQuotationPrice(string supplierId, string woodType, double thickness, string grade)
    {
        if (string.IsNullOrEmpty(supplierId)) return 0;
        var active = AppState.Quotations.FirstOrDefault(q => q.SupplierId == supplierId);
        var item = active?.Items.FirstOrDefault(i =>
            string.Equals(i.WoodType, woodType, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(i.Thickness - thickness) < 0.0001
            && string.Equals(i.Grade, grade?.Trim(), StringComparison.OrdinalIgnoreCase));
        return item?.PriceUsd ?? 0;
    }

    private void Recalculate(DraftLot lot)
    {
        var quotPrice = LookupQuotationPrice(SelectedSupplierId, lot.WoodType, D(lot.Thickness), lot.Grade);
        lot.PriceFromQuotation = quotPrice > 0;
        lot.EffectivePriceUsd = quotPrice > 0 ? quotPrice : (decimal)D(lot.ManualPriceUsd);
        lot.Cbm = WoodVolumeCalculator.CalculateVolume(lot.WoodType, D(lot.Thickness), D(lot.Width),
            D(lot.Length), (int)D(lot.Quantity), D(lot.Footage));
        lot.CostPriceVnd = WoodVolumeCalculator.CalculateCostPricePerM3(lot.EffectivePriceUsd,
            (decimal)D(lot.ExchangeRate), (decimal)D(lot.TaxPercent));
        lot.TotalValueVnd = WoodVolumeCalculator.CalculateTotalValue(lot.CostPriceVnd, lot.Cbm);
    }

    // ---------------- Bảng khai báo ----------------

    private void RebuildLotRows()
    {
        LotRowsPanel.Items.Clear();
        foreach (var lot in _draftLots)
            LotRowsPanel.Items.Add(BuildLotRow(lot));
        UpdateTotals();
    }

    private FrameworkElement BuildLotRow(DraftLot lot)
    {
        Recalculate(lot);

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        foreach (var w in new[] { 105.0, 115, 65, 75, 180, 75, 90, 115, -1, 50 })
            grid.ColumnDefinitions.Add(w < 0
                ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 }
                : new ColumnDefinition { Width = new GridLength(w) });

        // Kết quả (tạo trước để các handler cập nhật được)
        var cbmText = new TextBlock
        {
            Text = Fmt.M3(lot.Cbm), FontFamily = (FontFamily)FindResource("FontMono"),
            FontWeight = FontWeights.Medium, Foreground = (Brush)FindResource("Slate600"),
            Margin = new Thickness(6, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        var costText = new TextBlock
        {
            Text = Fmt.Vnd(lot.CostPriceVnd), FontFamily = (FontFamily)FindResource("FontMono"),
            Foreground = (Brush)FindResource("Slate800"),
            Margin = new Thickness(6, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        var priceBox = new TextBox
        {
            Style = (Style)FindResource("CellInputMono"),
            Text = lot.PriceFromQuotation ? Fmt.Num((double)lot.EffectivePriceUsd) : lot.ManualPriceUsd,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            IsReadOnly = lot.PriceFromQuotation,
            Foreground = lot.PriceFromQuotation
                ? (Brush)FindResource("Emerald600")
                : (Brush)FindResource("Rose500"),
            FontWeight = lot.PriceFromQuotation ? FontWeights.SemiBold : FontWeights.Normal
        };

        void Update()
        {
            Recalculate(lot);
            cbmText.Text = Fmt.M3(lot.Cbm);
            costText.Text = Fmt.Vnd(lot.CostPriceVnd);
            if (lot.PriceFromQuotation)
            {
                priceBox.IsReadOnly = true;
                priceBox.Foreground = (Brush)FindResource("Emerald600");
                priceBox.FontWeight = FontWeights.SemiBold;
                var autoText = Fmt.Num((double)lot.EffectivePriceUsd);
                if (priceBox.Text != autoText) priceBox.Text = autoText;
            }
            else
            {
                priceBox.IsReadOnly = false;
                priceBox.Foreground = (Brush)FindResource("Rose500");
                priceBox.FontWeight = FontWeights.Normal;
            }
            UpdateTotals();
        }
        _rowUpdaters.Add(Update);

        priceBox.TextChanged += (_, _) =>
        {
            if (!lot.PriceFromQuotation) { lot.ManualPriceUsd = priceBox.Text; Update(); }
        };

        // Mã kiện
        var idBox = Cell(lot.Id, s => { lot.Id = s; }, mono: true, bold: true);
        idBox.Margin = new Thickness(12, 0, 6, 0);
        Grid.SetColumn(idBox, 0); grid.Children.Add(idBox);

        // Loại gỗ
        var typeCombo = new ComboBox
        {
            Style = (Style)FindResource("Select"),
            Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
        };
        StackPanel dimPanel = null;
        foreach (var t in WoodTypes)
            typeCombo.Items.Add(new ComboBoxItem { Content = t, Tag = t, IsSelected = t == lot.WoodType });
        typeCombo.SelectionChanged += (_, _) =>
        {
            lot.WoodType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? lot.WoodType;
            if (dimPanel != null) BuildDimPanel(dimPanel, lot, Update);
            Update();
        };
        Grid.SetColumn(typeCombo, 1); grid.Children.Add(typeCombo);

        // Dày
        var thickBox = Cell(lot.Thickness, s => { lot.Thickness = s; Update(); }, mono: true, center: true);
        thickBox.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(thickBox, 2); grid.Children.Add(thickBox);

        // Grade
        var gradeBox = Cell(lot.Grade, s => { lot.Grade = s; Update(); }, mono: true, center: true);
        gradeBox.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(gradeBox, 3); grid.Children.Add(gradeBox);

        // Rộng x dài / footage
        dimPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
        };
        BuildDimPanel(dimPanel, lot, Update);
        Grid.SetColumn(dimPanel, 4); grid.Children.Add(dimPanel);

        // Số lượng
        var qtyBox = Cell(lot.Quantity, s => { lot.Quantity = s; Update(); }, mono: true, center: true);
        qtyBox.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(qtyBox, 5); grid.Children.Add(qtyBox);

        Grid.SetColumn(cbmText, 6); grid.Children.Add(cbmText);
        Grid.SetColumn(priceBox, 7); grid.Children.Add(priceBox);
        Grid.SetColumn(costText, 8); grid.Children.Add(costText);

        // Xóa
        var del = new Button
        {
            Style = (Style)FindResource("BtnIconDanger"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12 }
        };
        del.Click += (_, _) =>
        {
            _draftLots.Remove(lot);
            RebuildLotRows();
        };
        Grid.SetColumn(del, 9); grid.Children.Add(del);

        return new Border
        {
            BorderBrush = (Brush)FindResource("Slate100"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.White,
            Child = grid
        };
    }

    private readonly List<Action> _rowUpdaters = new();

    private void BuildDimPanel(StackPanel panel, DraftLot lot, Action update)
    {
        panel.Children.Clear();
        if (lot.WoodType == "Gỗ Dương")
        {
            var footBox = Cell(lot.Footage, s => { lot.Footage = s; update(); }, mono: true);
            footBox.Width = 90;
            panel.Children.Add(footBox);
            panel.Children.Add(new TextBlock
            {
                Text = "BFT", FontSize = 10, Foreground = (Brush)FindResource("Slate400"),
                Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
        }
        else
        {
            var widthBox = Cell(lot.Width, s => { lot.Width = s; update(); }, mono: true, center: true);
            widthBox.Width = 56;
            var lengthBox = Cell(lot.Length, s => { lot.Length = s; update(); }, mono: true, center: true);
            lengthBox.Width = 66;
            panel.Children.Add(widthBox);
            panel.Children.Add(new TextBlock
            {
                Text = "x", Foreground = (Brush)FindResource("Slate500"),
                Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(lengthBox);
        }
    }

    private TextBox Cell(string initial, Action<string> onChange, bool mono = false, bool center = false, bool bold = false)
    {
        var box = new TextBox
        {
            Style = (Style)FindResource(mono ? "CellInputMono" : "CellInput"),
            Text = initial,
            VerticalAlignment = VerticalAlignment.Center
        };
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

    private void BtnAddLotRow_Click(object sender, RoutedEventArgs e)
    {
        _draftLots.Add(new DraftLot
        {
            Id = $"LOT-NEW-{_draftLots.Count + 1}",
            Quantity = "100"
        });
        RebuildLotRows();
    }

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
        => AddFormPanel.Visibility = AddFormPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
        => AddFormPanel.Visibility = Visibility.Collapsed;

    private void BtnSaveReceipt_Click(object sender, RoutedEventArgs e)
    {
        var supplierId = SelectedSupplierId;
        var invoice = (FInvoice.Text ?? "").Trim();
        var packingList = (FPackingList.Text ?? "").Trim();

        if (supplierId.Length == 0 || invoice.Length == 0 || packingList.Length == 0)
        {
            MessageBox.Show("Vui lòng nhập đầy đủ thông tin chứng từ.", "TimberFlow ERP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_draftLots.Count == 0)
        {
            MessageBox.Show("Phiếu nhập kho phải chứa ít nhất một kiện gỗ.", "TimberFlow ERP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!DateTime.TryParseExact(FDate.Text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            date = DateTime.Today;

        var ids = _draftLots.Select(l => (l.Id ?? "").Trim().ToUpperInvariant()).ToList();
        var duplicates = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            MessageBox.Show(
                $"Mã kiện gỗ bị trùng lặp trong phiếu: {string.Join(", ", duplicates)}. " +
                "Mỗi kiện gỗ phải có một mã định danh duy nhất.",
                "TimberFlow ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var receiptId = $"REC-{Random.Shared.Next(10000, 99999)}";
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
            receipt.Lots.Add(new WoodLot
            {
                Id = d.Id.Trim().ToUpperInvariant(),
                SupplierId = supplierId,
                ImportDate = date,
                ReceiptId = receiptId,
                Invoice = invoice,
                PackingList = packingList,
                WoodType = d.WoodType,
                WoodName = $"{d.WoodType} {d.Grade} {Fmt.Num(D(d.Thickness))}mm",
                ThicknessMm = D(d.Thickness),
                WidthMm = D(d.Width),
                LengthMm = D(d.Length),
                OriginalQuantity = quantity,
                Quantity = quantity,
                Footage = D(d.Footage),
                Cbm = d.Cbm,
                RemainingCbm = d.Cbm,
                PriceUsd = d.EffectivePriceUsd,
                ExchangeRate = (decimal)D(d.ExchangeRate),
                TaxPercent = (decimal)D(d.TaxPercent),
                CostPriceVnd = d.CostPriceVnd,
                TotalValueVnd = d.TotalValueVnd,
                Grade = string.IsNullOrWhiteSpace(d.Grade) ? "FAS" : d.Grade.Trim()
            });
        }

        try
        {
            AppState.AddReceipt(receipt);
            AddFormPanel.Visibility = Visibility.Collapsed;
            FInvoice.Text = "";
            FPackingList.Text = "";
            ResetDraft();
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
        public string InvoiceTag => $"Inv: {Receipt.Invoice}";
        public string PackingTag => $"PL: {Receipt.PackingList}";
        public string LotCountText => $"{Receipt.Lots.Count} kiện";
        public double Vol { get; }
        public string VolText => $"{Fmt.M3(Vol)} m³";
        public decimal Val { get; }
        public string ValText => Fmt.Vnd(Val);
        public RecRow(WarehouseReceipt r)
        {
            Receipt = r;
            SupplierName = AppState.FindSupplier(r.SupplierId)?.Name ?? "Unknown";
            Vol = r.Lots.Sum(l => l.Cbm);
            Val = r.Lots.Sum(l => l.TotalValueVnd);
        }
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

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TimberFlowDesktop.Data;
using TimberFlowDesktop.Domain;
using TimberFlowDesktop.Helpers;

namespace TimberFlowDesktop.Views;

public partial class LotsView : UserControl, IModuleView
{
    private sealed class HistoryRow
    {
        public string IssueText { get; set; }
        public string QtyText { get; set; }
        public string OrderText { get; set; }
        public string CbmText { get; set; }
        public string DateText { get; set; }
    }

    private bool _loading;
    private string _selectedLotId;

    public LotsView()
    {
        InitializeComponent();
        InitStaticCombos();
        RefreshView();
        Helpers.GridLayoutStore.Attach(LotGrid, "lots");
    }

    private void InitStaticCombos()
    {
        FWoodType.Items.Add(new ComboBoxItem { Content = "Gỗ Sồi", Tag = "Gỗ Sồi", IsSelected = true });
        FWoodType.Items.Add(new ComboBoxItem { Content = "Gỗ Dương (Poplar)", Tag = "Gỗ Dương" });
        FWoodType.Items.Add(new ComboBoxItem { Content = "Gỗ Tần Bì", Tag = "Gỗ Tần Bì" });
        FWoodType.Items.Add(new ComboBoxItem { Content = "Gỗ Thông", Tag = "Gỗ Thông" });
        FWoodType.Items.Add(new ComboBoxItem { Content = "Gỗ Tràm", Tag = "Gỗ Tràm" });
    }

    public void RefreshView()
    {
        _loading = true;

        // Bộ lọc loại gỗ
        var currentType = (FilterWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterWoodType.Items.Clear();
        FilterWoodType.Items.Add(new ComboBoxItem { Content = "Tất cả loại gỗ", Tag = "ALL" });
        foreach (var t in AppState.Lots.Select(l => l.WoodType).Distinct())
            FilterWoodType.Items.Add(new ComboBoxItem { Content = t, Tag = t });
        SelectByTag(FilterWoodType, currentType);

        // Bộ lọc NCC
        var currentSup = (FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterSupplier.Items.Clear();
        FilterSupplier.Items.Add(new ComboBoxItem { Content = "Tất cả nhà cung cấp", Tag = "ALL" });
        foreach (var s in AppState.Suppliers)
            FilterSupplier.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id });
        SelectByTag(FilterSupplier, currentSup);

        // NCC trong form khai báo
        var currentFormSup = (FSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        FSupplier.Items.Clear();
        FSupplier.Items.Add(new ComboBoxItem { Content = "-- Chọn NCC --", Tag = "" });
        foreach (var s in AppState.Suppliers)
            FSupplier.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id });
        SelectByTag(FSupplier, currentFormSup);

        _loading = false;

        RebuildRows();
        UpdateCalcPreview();
        RefreshDetail();
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if ((item.Tag as string) == tag) { combo.SelectedItem = item; return; }
        }
        combo.SelectedIndex = 0;
    }

    private List<WoodLot> FilteredLots()
    {
        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        var type = (FilterWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        var sup = (FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";

        return AppState.Lots.Where(l =>
        {
            var matchSearch = term.Length == 0
                || l.Id.ToLowerInvariant().Contains(term)
                || (l.WoodName ?? "").ToLowerInvariant().Contains(term)
                || (l.Invoice ?? "").ToLowerInvariant().Contains(term);
            var matchType = type == "ALL" || l.WoodType == type;
            var matchSup = sup == "ALL" || l.SupplierId == sup;
            return matchSearch && matchType && matchSup;
        }).ToList();
    }

    // ---------------- Bảng (DataGrid tương tác) ----------------

    public sealed class LotRow
    {
        public WoodLot Lot { get; }
        public string Id => Lot.Id;
        public string WoodName => Lot.WoodName;
        public string InvoiceLabel => $"Invoice: {Lot.Invoice}";
        public double Thickness => Lot.ThicknessMm;
        public string ThicknessText => Fmt.Num(Lot.ThicknessMm);
        public bool IsPoplar => Lot.WoodType == "Gỗ Dương";
        public string DimText => IsPoplar ? "Đo theo Footage" : $"{Fmt.Num(Lot.WidthMm)} x {Fmt.Num(Lot.LengthMm)}";
        public int Quantity => Lot.Quantity;
        public bool IsLow => Lot.Quantity <= 30;
        public string QtyText => $"{Lot.Quantity} / {Lot.OriginalQuantity} thanh";
        public double RemainingCbm => Lot.RemainingCbm;
        public string VolText => $"{Fmt.M3(Lot.RemainingCbm)} m³";
        public decimal CostPriceVnd => Lot.CostPriceVnd;
        public string CostText => Fmt.Vnd(Lot.CostPriceVnd);
        public decimal TotalValueVnd => Lot.TotalValueVnd;
        public string ValText => Fmt.Vnd(Lot.TotalValueVnd);
        public LotRow(WoodLot l) => Lot = l;
    }

    private readonly List<LotRow> _rows = new();
    private ICollectionView _view;

    private void RebuildRows()
    {
        _rows.Clear();
        foreach (var l in AppState.Lots) _rows.Add(new LotRow(l));

        if (_view == null)
        {
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterPredicate;
            LotGrid.ItemsSource = _view;
        }
        _view.Refresh();
        UpdateTotalsAndEmpty();

        // Chọn lại dòng đang xem (nếu còn)
        LotGrid.SelectedItem = _rows.FirstOrDefault(r => r.Lot.Id == _selectedLotId);
    }

    private bool FilterPredicate(object o)
    {
        var l = ((LotRow)o).Lot;
        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        var type = (FilterWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        var sup = (FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";

        var matchSearch = term.Length == 0
            || l.Id.ToLowerInvariant().Contains(term)
            || (l.WoodName ?? "").ToLowerInvariant().Contains(term)
            || (l.Invoice ?? "").ToLowerInvariant().Contains(term);
        var matchType = type == "ALL" || l.WoodType == type;
        var matchSup = sup == "ALL" || l.SupplierId == sup;
        return matchSearch && matchType && matchSup;
    }

    private void UpdateTotalsAndEmpty()
    {
        var rows = _view.Cast<LotRow>().ToList();
        TotalQty.Text = $"{rows.Sum(r => r.Lot.Quantity).ToString("N0", CultureInfo.GetCultureInfo("en-US"))} thanh";
        TotalVol.Text = $"{Fmt.M3(rows.Sum(r => r.Lot.RemainingCbm))} m³";
        TotalVal.Text = Fmt.Vnd(rows.Sum(r => r.Lot.TotalValueVnd));
        EmptyRow.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LotGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedLotId = (LotGrid.SelectedItem as LotRow)?.Lot.Id;
        RefreshDetail();
    }

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LotRow r) LotGrid.SelectedItem = r;
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LotRow r) DeleteLot(r.Lot);
    }

    private void DeleteLot(WoodLot lot)
    {
        var confirm = MessageBox.Show($"Bạn có chắc muốn xóa Kiện gỗ {lot.Id} khỏi hệ thống?",
            "TimberFlow ERP", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (_selectedLotId == lot.Id) _selectedLotId = null;
            AppState.DeleteLot(lot.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------- Panel chi tiết ----------------

    private void SelectLot(string lotId)
    {
        _selectedLotId = lotId;
        RebuildRows();
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        var lot = AppState.Lots.FirstOrDefault(l => l.Id == _selectedLotId);
        if (lot == null)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DetailPanel.Visibility = Visibility.Visible;
        DetailTitle.Text = $"Truy xuất kiện: {lot.Id}";
        DWoodType.Text = lot.WoodType;
        DGrade.Text = lot.Grade;
        DSpec.Text = $"{Fmt.Num(lot.ThicknessMm)} x {Fmt.Num(lot.WidthMm)} x {Fmt.Num(lot.LengthMm)}";
        DFootageRow.Visibility = lot.WoodType == "Gỗ Dương" ? Visibility.Visible : Visibility.Collapsed;
        DFootage.Text = $"{Fmt.Num(lot.Footage)} BFT";
        DFormula.Text = lot.WoodType == "Gỗ Dương"
            ? $"({Fmt.Num(lot.Footage)} / 1000) * 2.36 = {Fmt.Num(lot.Cbm)} m³"
            : $"({Fmt.Num(lot.ThicknessMm)} * {Fmt.Num(lot.WidthMm)} * {Fmt.Num(lot.LengthMm)} * {lot.OriginalQuantity}) / 1,000,000,000 = {Fmt.Num(lot.Cbm)} m³";
        DPriceUsd.Text = $"${Fmt.Num((double)lot.PriceUsd)} / m³";
        DExchangeRate.Text = Fmt.N0(lot.ExchangeRate);
        DTax.Text = $"{Fmt.Num((double)lot.TaxPercent)}%";
        DCost.Text = Fmt.Vnd(lot.CostPriceVnd);
        DInvoice.Text = lot.Invoice;
        DPackingList.Text = lot.PackingList;
        DReceiptId.Text = lot.ReceiptId;

        var history = new List<HistoryRow>();
        foreach (var issue in AppState.Issues)
        {
            var item = issue.Items.FirstOrDefault(i => i.WoodLotId == lot.Id);
            if (item == null) continue;
            history.Add(new HistoryRow
            {
                IssueText = $"Phiếu xuất: {issue.Id}",
                QtyText = $"-{item.Quantity} thanh",
                OrderText = $"Đơn hàng: {issue.OrderId}",
                CbmText = $"{Fmt.M3(item.Cbm)} m³",
                DateText = $"Ngày xuất: {Fmt.Date(issue.Date)}"
            });
        }
        DHistoryEmpty.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DHistoryList.ItemsSource = history;
    }

    private void BtnCloseDetail_Click(object sender, RoutedEventArgs e)
    {
        _selectedLotId = null;
        RebuildRows();
        RefreshDetail();
    }

    // ---------------- Form khai báo ----------------

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
        => AddFormPanel.Visibility = AddFormPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
        => AddFormPanel.Visibility = Visibility.Collapsed;

    private void FWoodType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PWidth == null || PFootage == null) return;
        var isPoplar = ((FWoodType.SelectedItem as ComboBoxItem)?.Tag as string) == "Gỗ Dương";
        PWidth.Visibility = isPoplar ? Visibility.Collapsed : Visibility.Visible;
        PLength.Visibility = isPoplar ? Visibility.Collapsed : Visibility.Visible;
        PFootage.Visibility = isPoplar ? Visibility.Visible : Visibility.Collapsed;
        UpdateCalcPreview();
    }

    private void Calc_Changed(object sender, TextChangedEventArgs e) => UpdateCalcPreview();

    private static double D(TextBox box) =>
        double.TryParse(box?.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private void UpdateCalcPreview()
    {
        if (CalcVolume == null) return;
        var woodType = (FWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "Gỗ Sồi";
        var volume = WoodVolumeCalculator.CalculateVolume(woodType, D(FThickness), D(FWidth), D(FLength),
            (int)D(FQuantity), D(FFootage));
        var cost = WoodVolumeCalculator.CalculateCostPricePerM3((decimal)D(FPriceUsd), (decimal)D(FExchangeRate),
            (decimal)D(FTaxPercent));
        var total = WoodVolumeCalculator.CalculateTotalValue(cost, volume);

        CalcVolume.Text = $"{Fmt.M3(volume)} m³";
        CalcCost.Text = Fmt.Vnd(cost);
        CalcTotal.Text = Fmt.Vnd(total);
    }

    private void BtnSubmitAdd_Click(object sender, RoutedEventArgs e)
    {
        var lotId = (FLotId.Text ?? "").Trim().ToUpperInvariant();
        var supplierId = (FSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        if (lotId.Length == 0)
        {
            MessageBox.Show("Vui lòng nhập Mã Kiện Gỗ.", "TimberFlow ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (supplierId.Length == 0)
        {
            MessageBox.Show("Vui lòng chọn Nhà Cung Cấp.", "TimberFlow ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var woodType = (FWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "Gỗ Sồi";
        var grade = string.IsNullOrWhiteSpace(FGrade.Text) ? "FAS" : FGrade.Text.Trim();
        var quantity = (int)D(FQuantity);
        var volume = WoodVolumeCalculator.CalculateVolume(woodType, D(FThickness), D(FWidth), D(FLength), quantity, D(FFootage));
        var cost = WoodVolumeCalculator.CalculateCostPricePerM3((decimal)D(FPriceUsd), (decimal)D(FExchangeRate), (decimal)D(FTaxPercent));

        var lot = new WoodLot
        {
            Id = lotId,
            SupplierId = supplierId,
            ImportDate = DateTime.Today,
            ReceiptId = "REC-MANUAL",
            Invoice = string.IsNullOrWhiteSpace(FInvoice.Text) ? "INV-MANUAL" : FInvoice.Text.Trim(),
            PackingList = "PL-MANUAL",
            WoodType = woodType,
            WoodName = $"{woodType} {grade} {Fmt.Num(D(FThickness))}mm",
            ThicknessMm = D(FThickness),
            WidthMm = D(FWidth),
            LengthMm = D(FLength),
            OriginalQuantity = quantity,
            Quantity = quantity,
            Footage = D(FFootage),
            Cbm = volume,
            RemainingCbm = volume,
            PriceUsd = (decimal)D(FPriceUsd),
            ExchangeRate = (decimal)D(FExchangeRate),
            TaxPercent = (decimal)D(FTaxPercent),
            CostPriceVnd = cost,
            TotalValueVnd = WoodVolumeCalculator.CalculateTotalValue(cost, volume),
            Grade = grade
        };

        try
        {
            AppState.AddLot(lot);
            AddFormPanel.Visibility = Visibility.Collapsed;
            FLotId.Text = "";
            FInvoice.Text = "";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Filters_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded || _view == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        UpdateTotalsAndEmpty();
    }
}

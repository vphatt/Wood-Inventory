using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WoodInventory.Data;
using WoodInventory.Domain;
using WoodInventory.Helpers;

namespace WoodInventory.Views;

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
        RefreshView();
        Helpers.GridLayoutStore.Attach(LotGrid, "lots");
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

        _loading = false;

        RebuildRows();
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
        public bool IsPoplar => AppState.GetVolumeRule(Lot.WoodType) == VolumeRule.ByFootage;
        public string DimText => IsPoplar
            ? (string.IsNullOrWhiteSpace(Lot.LengthNote) ? "Đo theo Footage" : $"Đo theo Footage ({Lot.LengthNote})")
            : $"{Fmt.Num(Lot.WidthMm)} x {Fmt.Num(Lot.LengthMm)}";
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
            ActionGrid.ItemsSource = _view;   // cột thao tác tách riêng, cùng nguồn
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
        TotalQty.Text = $"{Fmt.N0((double)rows.Sum(r => r.Lot.Quantity))} thanh";
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
            "Quản Lý Gỗ", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
        DWoodType.Text = string.IsNullOrWhiteSpace(lot.WoodSubType)
            ? lot.WoodType
            : $"{lot.WoodType} · {lot.WoodSubType}";
        DGrade.Text = lot.Grade;
        var isPoplarLot = AppState.GetVolumeRule(lot.WoodType) == VolumeRule.ByFootage;
        // Gỗ footage: quy cách chỉ có độ dày (ưu tiên ký hiệu inch); loại khác: Dày x Rộng x Dài.
        DSpec.Text = isPoplarLot
            ? "Dày " + (string.IsNullOrWhiteSpace(lot.ThicknessNote) ? $"{Fmt.Num(lot.ThicknessMm)}mm" : lot.ThicknessNote)
            : $"{Fmt.Num(lot.ThicknessMm)} x {Fmt.Num(lot.WidthMm)} x {Fmt.Num(lot.LengthMm)}";
        DFootageRow.Visibility = isPoplarLot ? Visibility.Visible : Visibility.Collapsed;
        DFootage.Text = $"{Fmt.Num(lot.Footage)} BFT";
        DLengthNoteRow.Visibility = string.IsNullOrWhiteSpace(lot.LengthNote) ? Visibility.Collapsed : Visibility.Visible;
        DLengthNote.Text = lot.LengthNote;
        DFormula.Text = isPoplarLot
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

    private void Filters_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded || _view == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        UpdateTotalsAndEmpty();
    }
}

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
        Helpers.GridPairSync.Link(LotGrid, ActionGrid);
    }

    public void RefreshView()
    {
        _loading = true;

        // Bộ lọc loại gỗ
        var currentType = (FilterWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterWoodType.Items.Clear();
        FilterWoodType.Items.Add(new ComboBoxItem { Content = Lang.T("Lots.Filter.AllTypes"), Tag = "ALL" });
        foreach (var t in AppState.Lots.Select(l => l.WoodType).Distinct())
            FilterWoodType.Items.Add(new ComboBoxItem { Content = t, Tag = t });
        SelectByTag(FilterWoodType, currentType);

        // Bộ lọc NCC
        var currentSup = (FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterSupplier.Items.Clear();
        FilterSupplier.Items.Add(new ComboBoxItem { Content = Lang.T("Receipts.Filter.AllSuppliers"), Tag = "ALL" });
        foreach (var s in AppState.Suppliers)
            FilterSupplier.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id });
        SelectByTag(FilterSupplier, currentSup);

        // Bộ lọc phân loại gỗ (con) — gộp mọi phân loại con đang tồn tại trong Tồn Kho
        var currentSubType = (FilterSubType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterSubType.Items.Clear();
        FilterSubType.Items.Add(new ComboBoxItem { Content = Lang.T("Lots.Filter.AllSubTypes"), Tag = "ALL" });
        foreach (var s in AppState.Lots.Select(l => l.WoodSubType).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
            FilterSubType.Items.Add(new ComboBoxItem { Content = s, Tag = s });
        SelectByTag(FilterSubType, currentSubType);

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
        public LotRow(WoodLot l) => Lot = l;

        public string Id => Lot.Id;

        // Chứng từ / nguồn gốc
        public string SupplierName => AppState.FindSupplier(Lot.SupplierId)?.Name ?? Lot.Supplier?.Name ?? "—";
        public string InvoiceText => string.IsNullOrWhiteSpace(Lot.Invoice) ? "—" : Lot.Invoice;
        public DateTime ImportDate => Lot.ImportDate;
        public string ImportDateText => Fmt.Date(Lot.ImportDate);
        public string DeliveryNoteText => string.IsNullOrWhiteSpace(Lot.DeliveryNote) ? "—" : Lot.DeliveryNote;

        // Loại gỗ
        public string WoodTypeText => Lot.WoodType;
        public string SubTypeText => string.IsNullOrWhiteSpace(Lot.WoodSubType) ? "—" : Lot.WoodSubType;
        public string GradeText => string.IsNullOrWhiteSpace(Lot.Grade) ? "—" : Lot.Grade;

        // Kích thước — gỗ footage: Dày/Dài dùng ký hiệu inch, Rộng vô nghĩa (—); gỗ quy cách: mm.
        public bool IsFootage => AppState.GetVolumeRule(Lot.WoodType) == VolumeRule.ByFootage;
        public string DayText => IsFootage
            ? (string.IsNullOrWhiteSpace(Lot.ThicknessNote) ? Fmt.Num(Lot.ThicknessMm) : Lot.ThicknessNote)
            : Fmt.Num(Lot.ThicknessMm);
        public string WidthText => IsFootage ? "—" : Fmt.Num(Lot.WidthMm);
        public string LengthText => IsFootage
            ? (string.IsNullOrWhiteSpace(Lot.LengthNote) ? "—" : Lot.LengthNote)
            : Fmt.Num(Lot.LengthMm);
        public string FootageText => IsFootage ? Fmt.Num(Lot.Footage) : "—";

        // Tồn kho
        public int Quantity => Lot.Quantity;
        public bool IsLow => Lot.Quantity <= AppState.Settings.LowStockThreshold && Lot.Quantity > 0;
        public string QtyText => $"{Lot.Quantity} / {Lot.OriginalQuantity} {Lang.T("Common.Unit.Bar")}";
        public string VolText => $"{Fmt.M3(Lot.RemainingCbm)} m³";

        // Khóa sắp xếp (số thật, không theo chuỗi hiển thị)
        public double ThicknessMm => Lot.ThicknessMm;
        public double WidthMm => Lot.WidthMm;
        public double LengthMm => Lot.LengthMm;
        public double Footage => Lot.Footage;
        public double RemainingCbm => Lot.RemainingCbm;
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
            // Mặc định sắp xếp theo ngày nhập kho tăng dần
            _view.SortDescriptions.Add(new SortDescription(nameof(LotRow.ImportDate), ListSortDirection.Ascending));
            LotGrid.ItemsSource = _view;
            ActionGrid.ItemsSource = _view;   // cột thao tác tách riêng, cùng nguồn
            ColLotDate.SortDirection = ListSortDirection.Ascending;   // hiện mũi tên sort mặc định
        }
        _view.Refresh();
        UpdateTotalsAndEmpty();

        // Chọn lại dòng đang xem (nếu còn)
        LotGrid.SelectedItem = _rows.FirstOrDefault(r => r.Lot.Id == _selectedLotId);
    }

    private bool FilterPredicate(object o)
    {
        var row = (LotRow)o;
        var l = row.Lot;
        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        var type = (FilterWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        var sup = (FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        var subType = (FilterSubType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";

        var matchSearch = term.Length == 0
            || l.Id.ToLowerInvariant().Contains(term)
            || (l.WoodName ?? "").ToLowerInvariant().Contains(term)
            || (l.Invoice ?? "").ToLowerInvariant().Contains(term)
            || row.SupplierName.ToLowerInvariant().Contains(term);
        var matchType = type == "ALL" || l.WoodType == type;
        var matchSup = sup == "ALL" || l.SupplierId == sup;
        var matchSubType = subType == "ALL" || l.WoodSubType == subType;

        // Bộ lọc theo từng cột (mỗi ô trống = bỏ qua, không trống = so khớp "chứa" trên đúng text hiển thị ở cột đó).
        bool Contains(string cellText, string filterBox) =>
            string.IsNullOrWhiteSpace(filterBox) ||
            (cellText ?? "").ToLowerInvariant().Contains(filterBox.Trim().ToLowerInvariant());

        var matchImportDate = FImportDateFilter.SelectedDate == null || l.ImportDate.Date == FImportDateFilter.SelectedDate.Value.Date;

        var matchColumns = matchImportDate &&
            Contains(row.InvoiceText, FInvoiceFilter.Text) &&
            Contains(row.DeliveryNoteText, FDeliveryNoteFilter.Text) &&
            Contains(row.Id, FIdFilter.Text) &&
            Contains(row.GradeText, FGradeFilter.Text) &&
            Contains(row.DayText, FDayFilter.Text) &&
            Contains(row.WidthText, FWidthFilter.Text) &&
            Contains(row.LengthText, FLengthFilter.Text) &&
            Contains(row.FootageText, FFootageFilter.Text) &&
            Contains(row.QtyText, FQtyFilter.Text) &&
            Contains(row.VolText, FVolFilter.Text);

        return matchSearch && matchType && matchSup && matchSubType && matchColumns;
    }

    private void UpdateTotalsAndEmpty()
    {
        var rows = _view.Cast<LotRow>().ToList();
        TotalQty.Text = $"{Fmt.N0((double)rows.Sum(r => r.Lot.Quantity))} {Lang.T("Common.Unit.Bar")}";
        TotalVol.Text = $"{Fmt.M3(rows.Sum(r => r.Lot.RemainingCbm))} m³";
        TotalVal.Text = Fmt.Vnd(rows.Sum(r => r.Lot.TotalValueVnd));
        EmptyRow.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        // Chỉ bấm icon "Xem" mới mở panel chi tiết bên phải (không mở khi chỉ click chọn dòng).
        if ((sender as FrameworkElement)?.DataContext is not LotRow r) return;
        _selectedLotId = r.Lot.Id;
        LotGrid.SelectedItem = r;
        RefreshDetail();
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LotRow r) DeleteLot(r.Lot);
    }

    private void DeleteLot(WoodLot lot)
    {
        var confirm = AppDialog.Show(Lang.T("Lots.Confirm.Delete", lot.Id),
            Lang.T("Common.AppTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (_selectedLotId == lot.Id) _selectedLotId = null;
            AppState.DeleteLot(lot.Id);
        }
        catch (Exception ex)
        {
            AppDialog.Show(ex.Message, Lang.T("Common.CannotDeleteTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
        DetailTitle.Text = Lang.T("Lots.Detail.Title", lot.Id);
        DWoodType.Text = string.IsNullOrWhiteSpace(lot.WoodSubType)
            ? lot.WoodType
            : $"{lot.WoodType} · {lot.WoodSubType}";
        DOrigin.Text = string.IsNullOrWhiteSpace(lot.Origin) ? "—" : lot.Origin;
        var isPoplarLot = AppState.GetVolumeRule(lot.WoodType) == VolumeRule.ByFootage;
        // Gỗ footage: quy cách chỉ có độ dày (ưu tiên ký hiệu inch); loại khác: Dày x Rộng x Dài.
        DSpec.Text = isPoplarLot
            ? Lang.T("Lots.Detail.SpecPrefix") + (string.IsNullOrWhiteSpace(lot.ThicknessNote) ? $"{Fmt.Num(lot.ThicknessMm)}mm" : lot.ThicknessNote)
            : $"{Fmt.Num(lot.ThicknessMm)} x {Fmt.Num(lot.WidthMm)} x {Fmt.Num(lot.LengthMm)}";
        DFootageRow.Visibility = isPoplarLot ? Visibility.Visible : Visibility.Collapsed;
        DFootage.Text = $"{Fmt.Num(lot.Footage)} BFT";
        DLengthNoteRow.Visibility = string.IsNullOrWhiteSpace(lot.LengthNote) ? Visibility.Collapsed : Visibility.Visible;
        DLengthNote.Text = lot.LengthNote;
        DFormula.Text = isPoplarLot
            ? $"({Fmt.Num(lot.Footage)} / 1000) * 2.36 = {Fmt.Num(lot.Cbm)} m³"
            : $"({Fmt.Num(lot.ThicknessMm)} * {Fmt.Num(lot.WidthMm)} * {Fmt.Num(lot.LengthMm)} * {lot.OriginalQuantity}) / 1,000,000,000 = {Fmt.Num(lot.Cbm)} m³";
        DPriceUsd.Text = $"{Fmt.Money(lot.Price, lot.PriceCurrency)} / m³";
        DExchangeRate.Text = Fmt.N0(lot.ExchangeRate);
        DTax.Text = $"{Fmt.Num((double)lot.TaxPercent)}%";

        // 3 dòng tiền tính theo LƯỢNG NHẬP BAN ĐẦU (Cbm gốc), không theo tồn hiện tại.
        var totalPrice = WoodVolumeCalculator.CalculateTotalPrice(lot.Price, lot.Cbm);
        var subtotal = WoodVolumeCalculator.ConvertToVnd(totalPrice, lot.ExchangeRate);
        var vat = WoodVolumeCalculator.CalculateTaxAmountVnd(subtotal, lot.TaxPercent);
        DSubtotal.Text = Fmt.Vnd(subtotal);
        DVat.Text = Fmt.Vnd(vat);
        DTotal.Text = Fmt.Vnd(subtotal + vat);
        // Giá trị vốn đã bị trừ khi xuất kho = (m³ đã xuất) × giá vốn/m³ (đã gồm thuế).
        var consumedCbm = Math.Max(0, lot.Cbm - lot.RemainingCbm);
        DUsed.Text = Fmt.Vnd((decimal)consumedCbm * lot.CostPriceVnd);

        DSupplier.Text = AppState.FindSupplier(lot.SupplierId)?.Name ?? lot.Supplier?.Name ?? "—";
        DInvoice.Text = string.IsNullOrWhiteSpace(lot.Invoice) ? "—" : lot.Invoice;
        DForestList.Text = string.IsNullOrWhiteSpace(lot.ForestList) ? "—" : lot.ForestList;
        DPackingList.Text = string.IsNullOrWhiteSpace(lot.PackingList) ? "—" : lot.PackingList;
        DDeliveryNote.Text = string.IsNullOrWhiteSpace(lot.DeliveryNote) ? "—" : lot.DeliveryNote;

        var history = new List<HistoryRow>();
        foreach (var issue in AppState.Issues)
        {
            var item = issue.Items.FirstOrDefault(i => i.WoodLotId == lot.Id);
            if (item == null) continue;
            history.Add(new HistoryRow
            {
                IssueText = Lang.T("Lots.Detail.HistoryIssue", issue.Id),
                QtyText = Lang.T("Lots.Detail.HistoryQty", item.Quantity, Lang.T("Common.Unit.Bar")),
                OrderText = Lang.T("Lots.Detail.HistoryOrder", issue.OrderId),
                CbmText = $"{Fmt.M3(item.Cbm)} m³",
                DateText = Lang.T("Lots.Detail.HistoryDate", Fmt.Date(issue.Date))
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
        BtnClearColumnFilters.Visibility = AnyColumnFilterActive() ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        UpdateTotalsAndEmpty();
    }

    // ---------------- Bộ lọc theo từng cột ----------------

    private bool AnyColumnFilterActive() =>
        !string.IsNullOrWhiteSpace(FInvoiceFilter.Text) || FImportDateFilter.SelectedDate != null ||
        !string.IsNullOrWhiteSpace(FDeliveryNoteFilter.Text) || !string.IsNullOrWhiteSpace(FIdFilter.Text) ||
        !string.IsNullOrWhiteSpace(FGradeFilter.Text) ||
        !string.IsNullOrWhiteSpace(FDayFilter.Text) || !string.IsNullOrWhiteSpace(FWidthFilter.Text) ||
        !string.IsNullOrWhiteSpace(FLengthFilter.Text) || !string.IsNullOrWhiteSpace(FFootageFilter.Text) ||
        !string.IsNullOrWhiteSpace(FQtyFilter.Text) || !string.IsNullOrWhiteSpace(FVolFilter.Text) ||
        ((FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL") != "ALL" ||
        ((FilterWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL") != "ALL" ||
        ((FilterSubType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL") != "ALL";

    private void BtnToggleColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        var expand = ColumnFilterPanel.Visibility != Visibility.Visible;
        ColumnFilterPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        ToggleColumnFiltersLabel.Text = expand ? Lang.T("Common.HideColumnFilter") : Lang.T("Common.FilterByColumn");
    }

    private void BtnClearColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        foreach (var box in new[] { FInvoiceFilter, FDeliveryNoteFilter, FIdFilter, FGradeFilter,
            FDayFilter, FWidthFilter, FLengthFilter, FFootageFilter, FQtyFilter, FVolFilter })
            box.Text = "";
        FImportDateFilter.SelectedDate = null;
        FilterSupplier.SelectedIndex = 0;
        FilterWoodType.SelectedIndex = 0;
        FilterSubType.SelectedIndex = 0;
        _loading = false;

        BtnClearColumnFilters.Visibility = Visibility.Collapsed;
        _view.Refresh();
        UpdateTotalsAndEmpty();
    }
}

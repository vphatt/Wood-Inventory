using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WoodInventory.Data;
using WoodInventory.Domain;
using WoodInventory.Helpers;

namespace WoodInventory.Views;

/// <summary>
/// Bảng tổng chi tiết nhập kho: liệt kê MỌI kiện gỗ đã nhập + thông tin tại thời điểm NHẬP
/// (số lượng/thể tích/tiền dùng lượng nhập ban đầu — Cbm gốc, KHÔNG phải tồn hiện tại).
/// </summary>
public partial class ReceiptReportView : UserControl
{
    public sealed class Row
    {
        public WoodLot Lot { get; }
        public Row(WoodLot l) => Lot = l;

        // Chứng từ nhập kho
        public string ReceiptId => Lot.ReceiptId;
        public string SupplierName => AppState.FindSupplier(Lot.SupplierId)?.Name ?? Lot.Supplier?.Name ?? "—";
        public string ImportDateText => Fmt.Date(Lot.ImportDate);
        public string InvoiceText => string.IsNullOrWhiteSpace(Lot.Invoice) ? "—" : Lot.Invoice;
        public string ForestListText => string.IsNullOrWhiteSpace(Lot.ForestList) ? "—" : Lot.ForestList;
        public string PackingListText => string.IsNullOrWhiteSpace(Lot.PackingList) ? "—" : Lot.PackingList;
        public string DeliveryNoteText => string.IsNullOrWhiteSpace(Lot.DeliveryNote) ? "—" : Lot.DeliveryNote;

        // Kiện
        public string Id => Lot.Id;
        public string WoodTypeText => Lot.WoodType;
        public string SubTypeText => string.IsNullOrWhiteSpace(Lot.WoodSubType) ? "—" : Lot.WoodSubType;
        public string OriginText => string.IsNullOrWhiteSpace(Lot.Origin) ? "—" : Lot.Origin;
        public string GradeText => string.IsNullOrWhiteSpace(Lot.Grade) ? "—" : Lot.Grade;

        public bool IsFootage => AppState.GetVolumeRule(Lot.WoodType) == VolumeRule.ByFootage;
        public string DayText => IsFootage
            ? (string.IsNullOrWhiteSpace(Lot.ThicknessNote) ? Fmt.Num(Lot.ThicknessMm) : Lot.ThicknessNote)
            : Fmt.Num(Lot.ThicknessMm);
        public string WidthText => IsFootage ? "—" : Fmt.Num(Lot.WidthMm);
        public string LengthText => IsFootage
            ? (string.IsNullOrWhiteSpace(Lot.LengthNote) ? "—" : Lot.LengthNote)
            : Fmt.Num(Lot.LengthMm);
        public string FootageText => IsFootage ? Fmt.Num(Lot.Footage) : "—";

        // Số lượng + thể tích lúc NHẬP (ban đầu)
        public string QtyText => $"{Fmt.N0((double)Lot.OriginalQuantity)} {Lang.T("Common.Unit.Bar")}";
        public string VolText => $"{Fmt.M3(Lot.Cbm)} m³";

        // Tài chính nhập kho (tính theo Cbm gốc)
        public string PriceText => Fmt.Money(Lot.Price, Lot.PriceCurrency);
        public string CurrencyText => Lot.PriceCurrency;
        public string ExRateText => Fmt.N0(Lot.ExchangeRate);
        public string TaxText => $"{Fmt.Num((double)Lot.TaxPercent)}%";

        public decimal SubtotalV => WoodVolumeCalculator.ConvertToVnd(
            WoodVolumeCalculator.CalculateTotalPrice(Lot.Price, Lot.Cbm), Lot.ExchangeRate);
        public decimal VatV => WoodVolumeCalculator.CalculateTaxAmountVnd(SubtotalV, Lot.TaxPercent);
        public string SubtotalText => Fmt.Vnd(SubtotalV);
        public string VatText => Fmt.Vnd(VatV);
        public string TotalText => Fmt.Vnd(SubtotalV + VatV);

        public int Qty => Lot.OriginalQuantity;
        public double CbmV => Lot.Cbm;
    }

    private readonly List<Row> _rows = new();
    private ICollectionView _view;
    private readonly Action _back;

    public ReceiptReportView(Action back)
    {
        InitializeComponent();
        _back = back;
        RefreshView();
        Helpers.GridLayoutStore.Attach(ReportGrid, "receipt-report");
    }

    public void RefreshView()
    {
        // Bộ lọc nhà cung cấp
        var curSup = (FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterSupplier.Items.Clear();
        FilterSupplier.Items.Add(new ComboBoxItem { Content = Lang.T("Receipts.Filter.AllSuppliers"), Tag = "ALL" });
        foreach (var s in AppState.Suppliers)
            FilterSupplier.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id });
        SelectByTag(FilterSupplier, curSup);

        _rows.Clear();
        foreach (var l in AppState.Lots
                     .OrderByDescending(l => l.ImportDate.Date)
                     .ThenBy(l => l.ReceiptId ?? "")
                     .ThenBy(l => l.Id))
            _rows.Add(new Row(l));

        if (_view == null)
        {
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterPredicate;
            ReportGrid.ItemsSource = _view;
        }
        _view.Refresh();
        UpdateTotals();
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem it in combo.Items)
            if ((it.Tag as string) == tag) { combo.SelectedItem = it; return; }
        combo.SelectedIndex = 0;
    }

    private bool FilterPredicate(object o)
    {
        var r = (Row)o;
        var l = r.Lot;
        var sup = (FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        if (sup != "ALL" && l.SupplierId != sup) return false;

        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        var matchSearch = term.Length == 0
            || (l.Id ?? "").ToLowerInvariant().Contains(term)
            || (l.ReceiptId ?? "").ToLowerInvariant().Contains(term)
            || (l.Invoice ?? "").ToLowerInvariant().Contains(term)
            || (l.WoodType ?? "").ToLowerInvariant().Contains(term)
            || (l.Origin ?? "").ToLowerInvariant().Contains(term);
        if (!matchSearch) return false;

        bool Contains(string cellText, string filterBox) =>
            string.IsNullOrWhiteSpace(filterBox) ||
            (cellText ?? "").ToLowerInvariant().Contains(filterBox.Trim().ToLowerInvariant());

        var matchDate = FImportDateFilter.SelectedDate == null || l.ImportDate.Date == FImportDateFilter.SelectedDate.Value.Date;

        return matchDate
            && Contains(r.ReceiptId, FReceiptIdFilter.Text) && Contains(r.InvoiceText, FInvoiceFilter.Text)
            && Contains(r.ForestListText, FForestListFilter.Text) && Contains(r.PackingListText, FPackingListFilter.Text)
            && Contains(r.Id, FIdFilter.Text) && Contains(r.DeliveryNoteText, FDeliveryNoteFilter.Text)
            && Contains(r.WoodTypeText, FWoodTypeFilter.Text) && Contains(r.SubTypeText, FSubTypeFilter.Text)
            && Contains(r.OriginText, FOriginFilter.Text) && Contains(r.GradeText, FGradeFilter.Text)
            && Contains(r.DayText, FDayFilter.Text)
            && Contains(r.WidthText, FWidthFilter.Text) && Contains(r.LengthText, FLengthFilter.Text)
            && Contains(r.FootageText, FFootageFilter.Text) && Contains(r.QtyText, FQtyFilter.Text)
            && Contains(r.VolText, FVolFilter.Text) && Contains(r.PriceText, FPriceFilter.Text)
            && Contains(r.CurrencyText, FCurrencyFilter.Text)
            && Contains(r.ExRateText, FExRateFilter.Text) && Contains(r.TaxText, FTaxFilter.Text)
            && Contains(r.SubtotalText, FSubtotalFilter.Text) && Contains(r.VatText, FVatFilter.Text)
            && Contains(r.TotalText, FTotalFilter.Text);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        BtnClearColumnFilters.Visibility = AnyColumnFilterActive() ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        UpdateTotals();
    }

    // ---------------- Bộ lọc theo từng cột ----------------

    private bool AnyColumnFilterActive() =>
        ((FilterSupplier.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL") != "ALL" ||
        FImportDateFilter.SelectedDate != null ||
        !string.IsNullOrWhiteSpace(FReceiptIdFilter.Text) || !string.IsNullOrWhiteSpace(FInvoiceFilter.Text) ||
        !string.IsNullOrWhiteSpace(FForestListFilter.Text) || !string.IsNullOrWhiteSpace(FPackingListFilter.Text) ||
        !string.IsNullOrWhiteSpace(FIdFilter.Text) || !string.IsNullOrWhiteSpace(FDeliveryNoteFilter.Text) ||
        !string.IsNullOrWhiteSpace(FWoodTypeFilter.Text) || !string.IsNullOrWhiteSpace(FSubTypeFilter.Text) ||
        !string.IsNullOrWhiteSpace(FOriginFilter.Text) || !string.IsNullOrWhiteSpace(FGradeFilter.Text) ||
        !string.IsNullOrWhiteSpace(FDayFilter.Text) ||
        !string.IsNullOrWhiteSpace(FWidthFilter.Text) || !string.IsNullOrWhiteSpace(FLengthFilter.Text) ||
        !string.IsNullOrWhiteSpace(FFootageFilter.Text) || !string.IsNullOrWhiteSpace(FQtyFilter.Text) ||
        !string.IsNullOrWhiteSpace(FVolFilter.Text) || !string.IsNullOrWhiteSpace(FPriceFilter.Text) ||
        !string.IsNullOrWhiteSpace(FCurrencyFilter.Text) ||
        !string.IsNullOrWhiteSpace(FExRateFilter.Text) || !string.IsNullOrWhiteSpace(FTaxFilter.Text) ||
        !string.IsNullOrWhiteSpace(FSubtotalFilter.Text) || !string.IsNullOrWhiteSpace(FVatFilter.Text) ||
        !string.IsNullOrWhiteSpace(FTotalFilter.Text);

    private void BtnToggleColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        var expand = ColumnFilterPanel.Visibility != Visibility.Visible;
        ColumnFilterPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        ToggleColumnFiltersLabel.Text = expand ? Lang.T("Common.HideColumnFilter") : Lang.T("Common.FilterByColumn");
    }

    private void BtnClearColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        FilterSupplier.SelectedIndex = 0;
        FImportDateFilter.SelectedDate = null;
        foreach (var box in new[]
                 {
                     FReceiptIdFilter, FInvoiceFilter, FForestListFilter, FPackingListFilter, FIdFilter,
                     FDeliveryNoteFilter, FWoodTypeFilter, FSubTypeFilter, FOriginFilter, FGradeFilter, FDayFilter,
                     FWidthFilter, FLengthFilter, FFootageFilter, FQtyFilter, FVolFilter, FPriceFilter,
                     FCurrencyFilter, FExRateFilter, FTaxFilter, FSubtotalFilter, FVatFilter, FTotalFilter
                 })
            box.Text = "";
        BtnClearColumnFilters.Visibility = Visibility.Collapsed;
        _view.Refresh();
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        var rows = _view.Cast<Row>().ToList();
        TotalLots.Text = Lang.T("Receipts.RecRow.LotCountText", Fmt.N0((double)rows.Count));
        TotalQty.Text = $"{Fmt.N0((double)rows.Sum(r => r.Qty))} {Lang.T("Common.Unit.Bar")}";
        TotalVol.Text = $"{Fmt.M3Total(rows.Sum(r => r.CbmV))} m³";
        TotalVal.Text = Fmt.Vnd(rows.Sum(r => r.SubtotalV + r.VatV));
        EmptyRow.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) => _back?.Invoke();
}

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

/// <summary>Trang chi tiết báo giá của MỘT nhà cung cấp: danh sách mục giá + search/filter + CRUD.</summary>
public partial class QuotationDetailView : UserControl
{
    public sealed class ItemRow
    {
        public QuotationItem Item { get; }
        public string WoodType => Item.WoodType;
        public string Grade => Item.Grade;
        public string GradeText => string.IsNullOrWhiteSpace(Item.Grade) ? "Bất kỳ" : Item.Grade;
        public string ThicknessText => Fmt.Range(Item.ThicknessMin, Item.ThicknessMax);
        public string WidthText => Fmt.Range(Item.WidthMin, Item.WidthMax);
        public string LengthText => Fmt.Range(Item.LengthMin, Item.LengthMax);
        public string Origin => Item.Origin;
        public string OriginText => string.IsNullOrWhiteSpace(Item.Origin) ? "Bất kỳ" : Item.Origin;
        public string Specification => Item.Specification;
        public decimal Price => Item.PriceUsd;
        public string PriceText => Fmt.Usd(Item.PriceUsd);
        public ItemRow(QuotationItem i) => Item = i;
    }

    private readonly Supplier _supplier;
    private readonly Action _back;
    private string _editingId;
    private string _mode = "add";   // add | view | edit
    private readonly List<ItemRow> _rows = new();
    private ICollectionView _view;

    public QuotationDetailView(Supplier supplier, Action back)
    {
        InitializeComponent();
        _supplier = supplier;
        _back = back;
        InitWoodTypeCombo();
        RefreshView();
        GridLayoutStore.Attach(ItemGrid, "quotation-items");
    }

    private void InitWoodTypeCombo()
    {
        FWoodType.Items.Clear();
        foreach (var name in AppState.CategoryNames)
            FWoodType.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        if (FWoodType.Items.Count > 0) FWoodType.SelectedIndex = 0;
    }

    public void RefreshView()
    {
        TitleName.Text = $"Báo giá — {_supplier.Name}";
        Subtitle.Text = $"Tên gọi tắt: {_supplier.Code}   •   Mã số thuế: {(string.IsNullOrWhiteSpace(_supplier.TaxCode) ? "—" : _supplier.TaxCode)}";

        var items = AppState.FindQuotation(_supplier.Id)?.Items ?? new List<QuotationItem>();
        _rows.Clear();
        foreach (var it in items) _rows.Add(new ItemRow(it));

        // Bộ lọc loại gỗ
        var currentType = (FilterWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterWoodType.Items.Clear();
        FilterWoodType.Items.Add(new ComboBoxItem { Content = "Tất cả loại gỗ", Tag = "ALL" });
        foreach (var t in items.Select(i => i.WoodType).Distinct())
            FilterWoodType.Items.Add(new ComboBoxItem { Content = t, Tag = t });
        SelectByTag(FilterWoodType, currentType);

        if (_view == null)
        {
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterPredicate;
            ItemGrid.ItemsSource = _view;
        }
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem it in combo.Items)
            if ((it.Tag as string) == tag) { combo.SelectedItem = it; return; }
        combo.SelectedIndex = 0;
    }

    private bool FilterPredicate(object o)
    {
        var r = (ItemRow)o;
        var type = (FilterWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        if (type != "ALL" && r.WoodType != type) return false;

        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        if (term.Length == 0) return true;
        return (r.WoodType ?? "").ToLowerInvariant().Contains(term)
            || (r.Grade ?? "").ToLowerInvariant().Contains(term)
            || (r.Origin ?? "").ToLowerInvariant().Contains(term)
            || (r.Specification ?? "").ToLowerInvariant().Contains(term);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    private void UpdateCountAndEmpty()
    {
        var n = _view.Cast<object>().Count();
        TotalCount.Text = n.ToString();
        EmptyRow.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- Cảnh báo inline ----------------

    private static void ShowWarn(TextBlock w, string msg) { w.Text = msg; w.Visibility = Visibility.Visible; }

    private void ClearWarnings() =>
        WThickRange.Visibility = WWidthRange.Visibility = WLengthRange.Visibility = WPrice.Visibility = Visibility.Collapsed;

    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TextBlock w) w.Visibility = Visibility.Collapsed;
    }

    // ---------------- Thêm / Xem / Sửa ----------------

    private static double D(string s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static string NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Parse 1 cặp Từ/Đến: trống = null (không giới hạn); validate số hợp lệ và Từ &lt;= Đến.</summary>
    private static bool ValidateRange(string minText, string maxText, TextBlock warn, string label, out double? min, out double? max)
    {
        var minOk = TryParseOptional(minText, out min);
        var maxOk = TryParseOptional(maxText, out max);
        if (!minOk || !maxOk) { ShowWarn(warn, $"{label}: giá trị không hợp lệ."); return false; }
        if (min != null && max != null && min > max) { ShowWarn(warn, $"{label}: giá trị 'Từ' phải nhỏ hơn hoặc bằng 'Đến'."); return false; }
        return true;
    }

    private static bool TryParseOptional(string text, out double? value)
    {
        if (string.IsNullOrWhiteSpace(text)) { value = null; return true; }
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v >= 0)
        {
            value = v;
            return true;
        }
        value = null;
        return false;
    }

    private void SetReadOnly(bool ro)
    {
        FGrade.IsReadOnly = FOrigin.IsReadOnly = FSpec.IsReadOnly = FPrice.IsReadOnly = ro;
        FThickMin.IsReadOnly = FThickMax.IsReadOnly = FWidthMin.IsReadOnly = FWidthMax.IsReadOnly = FLengthMin.IsReadOnly = FLengthMax.IsReadOnly = ro;
        var bg = ro ? (Brush)FindResource("Slate50") : Brushes.White;
        FGrade.Background = FOrigin.Background = FSpec.Background = FPrice.Background = bg;
        FThickMin.Background = FThickMax.Background = FWidthMin.Background = FWidthMax.Background = FLengthMin.Background = FLengthMax.Background = bg;
        FWoodType.IsEnabled = !ro;
    }

    private void EnterAddMode()
    {
        _mode = "add";
        _editingId = null;
        ClearWarnings();
        SetReadOnly(false);
        FormTitle.Text = "Thêm Mục Giá Mới";
        FormSaveBtn.Content = "Lưu mục giá";
        if (FWoodType.Items.Count > 0) FWoodType.SelectedIndex = 0;
        FGrade.Text = FOrigin.Text = FSpec.Text = FPrice.Text = "";
        FThickMin.Text = FThickMax.Text = FWidthMin.Text = FWidthMax.Text = FLengthMin.Text = FLengthMax.Text = "";
    }

    private static string NumOrBlank(double? v) => v.HasValue ? Fmt.Num(v.Value) : "";

    private void FillForm(QuotationItem it)
    {
        SelectByTag(FWoodType, it.WoodType);
        FGrade.Text = it.Grade;
        FOrigin.Text = it.Origin;
        FThickMin.Text = NumOrBlank(it.ThicknessMin);
        FThickMax.Text = NumOrBlank(it.ThicknessMax);
        FWidthMin.Text = NumOrBlank(it.WidthMin);
        FWidthMax.Text = NumOrBlank(it.WidthMax);
        FLengthMin.Text = NumOrBlank(it.LengthMin);
        FLengthMax.Text = NumOrBlank(it.LengthMax);
        FSpec.Text = it.Specification;
        FPrice.Text = Fmt.Num((double)it.PriceUsd);
    }

    private void EnterViewMode(QuotationItem it)
    {
        _mode = "view";
        _editingId = it.Id;
        ClearWarnings();
        FillForm(it);
        SetReadOnly(true);
        FormTitle.Text = $"Chi Tiết Mục Giá — {it.WoodType} ({Fmt.Range(it.ThicknessMin, it.ThicknessMax)})";
        FormSaveBtn.Content = "Chỉnh sửa";
        AddFormPanel.Visibility = Visibility.Visible;
    }

    private void EnterEditMode()
    {
        _mode = "edit";
        ClearWarnings();
        SetReadOnly(false);
        FormTitle.Text = "Sửa Mục Giá";
        FormSaveBtn.Content = "Cập nhật";
        FGrade.Focus();
    }

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        if (AddFormPanel.Visibility == Visibility.Visible && _mode == "add")
        {
            AddFormPanel.Visibility = Visibility.Collapsed;
            return;
        }
        EnterAddMode();
        AddFormPanel.Visibility = Visibility.Visible;
    }

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
    {
        AddFormPanel.Visibility = Visibility.Collapsed;
        EnterAddMode();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == "view") { EnterEditMode(); return; }

        ClearWarnings();
        var ok = true;
        if (!ValidateRange(FThickMin.Text, FThickMax.Text, WThickRange, "Độ dày", out var thickMin, out var thickMax)) ok = false;
        if (!ValidateRange(FWidthMin.Text, FWidthMax.Text, WWidthRange, "Rộng", out var widthMin, out var widthMax)) ok = false;
        if (!ValidateRange(FLengthMin.Text, FLengthMax.Text, WLengthRange, "Dài", out var lengthMin, out var lengthMax)) ok = false;
        if (D(FPrice.Text) <= 0) { ShowWarn(WPrice, "Đơn giá phải lớn hơn 0."); ok = false; }
        if (!ok) return;

        var item = new QuotationItem
        {
            Id = _editingId,
            WoodType = (FWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
            Grade = NullIfBlank(FGrade.Text),
            ThicknessMin = thickMin,
            ThicknessMax = thickMax,
            WidthMin = widthMin,
            WidthMax = widthMax,
            LengthMin = lengthMin,
            LengthMax = lengthMax,
            Origin = NullIfBlank(FOrigin.Text),
            Specification = NullIfBlank(FSpec.Text),
            PriceUsd = (decimal)D(FPrice.Text)
        };

        try
        {
            if (_editingId == null) AppState.AddQuotationItem(_supplier.Id, item);
            else AppState.UpdateQuotationItem(item);
            AddFormPanel.Visibility = Visibility.Collapsed;
            EnterAddMode();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ItemRow r) EnterViewMode(r.Item);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ItemRow r) return;
        var confirm = MessageBox.Show($"Xóa mục giá {r.WoodType} ({r.ThicknessText}) khỏi báo giá?",
            "TimberFlow ERP", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        AppState.DeleteQuotationItem(r.Item.Id);
        if (_editingId == r.Item.Id) { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) => _back?.Invoke();
}

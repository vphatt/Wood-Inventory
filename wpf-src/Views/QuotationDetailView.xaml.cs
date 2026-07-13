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

/// <summary>Trang chi tiết báo giá của MỘT nhà cung cấp: danh sách báo giá + search/filter + CRUD.</summary>
public partial class QuotationDetailView : UserControl
{
    public sealed class ItemRow
    {
        public QuotationItem Item { get; }
        public string WoodType => Item.WoodType;
        public string SubType => Item.WoodSubType;
        public string WoodTypeDisplay => string.IsNullOrWhiteSpace(Item.WoodSubType)
            ? Item.WoodType
            : $"{Item.WoodType} · {Item.WoodSubType}";
        public string Grade => Item.Grade;
        public string GradeText => string.IsNullOrWhiteSpace(Item.Grade) ? "-" : Item.Grade;
        public string ThicknessText => AppState.GetVolumeRule(Item.WoodType) == VolumeRule.ByFootage
            ? Fmt.RangeNote(Item.ThicknessMinNote, Item.ThicknessMaxNote, Item.ThicknessMin, Item.ThicknessMax)
            : Fmt.RangeOrList(Item.ThicknessValues, Item.ThicknessMin, Item.ThicknessMax);
        public string WidthText => Fmt.RangeOrList(Item.WidthValues, Item.WidthMin, Item.WidthMax);
        public string LengthText => Fmt.RangeOrList(Item.LengthValues, Item.LengthMin, Item.LengthMax);
        // Khóa sắp xếp cho cột kích thước (số thật, không theo chuỗi range hiển thị)
        public double? ThicknessMin => Item.ThicknessMin;
        public double? WidthMin => Item.WidthMin;
        public double? LengthMin => Item.LengthMin;
        public string Origin => Item.Origin;
        public string OriginText => string.IsNullOrWhiteSpace(Item.Origin) ? "-" : Item.Origin;
        public string Specification => Item.Specification;
        public decimal Price => Item.Price;
        public string PriceCurrency => Item.PriceCurrency;
        public string PriceText => Fmt.Money(Item.Price, Item.PriceCurrency);
        public DateTime? Updated => Item.UpdatedAt;
        public string UpdatedAtText => Item.UpdatedAt.HasValue
            ? Item.UpdatedAt.Value.ToString("yyyy/MM/dd HH:mm:ss")
            : "—";
        public ItemRow(QuotationItem i) => Item = i;
    }

    /// <summary>3 chế độ nhập Dày/Rộng/Dài, chọn qua chip radio cạnh label — quyết định field nào hiện +
    /// cách parse/lưu khi bấm Lưu (xem <see cref="ValidateDimension"/>).</summary>
    private enum DimMode { Single, Range, List }

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
        GridPairSync.Link(ItemGrid, ActionGrid);
    }

    private void InitWoodTypeCombo()
    {
        FWoodType.Items.Clear();
        foreach (var name in AppState.CategoryNames)
            FWoodType.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        FWoodType.SelectionChanged += (_, _) =>
        {
            PopulateSubCombo((FWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "");
            SyncThicknessForWoodType();
        };
        FWoodSubType.SelectionChanged += (_, _) => WSubType.Visibility = Visibility.Collapsed;
        if (FWoodType.Items.Count > 0) FWoodType.SelectedIndex = 0;
    }

    /// <summary>Đổ danh sách phân loại con theo loại gỗ cha (nối tầng), chọn sẵn <paramref name="selectSub"/>.</summary>
    private void PopulateSubCombo(string woodType, string selectSub = null)
    {
        FWoodSubType.Items.Clear();
        FWoodSubType.Items.Add(new ComboBoxItem { Content = Lang.T("Quotations.SubTypePlaceholder"), Tag = "" });
        foreach (var s in AppState.SubNamesOf(woodType))
            FWoodSubType.Items.Add(new ComboBoxItem { Content = s, Tag = s });
        SelectByTag(FWoodSubType, selectSub ?? "");
    }

    public void RefreshView()
    {
        TitleName.Text = Lang.T("Quotations.TitleName", _supplier.Name);
        Subtitle.Text = Lang.T("Quotations.Subtitle", _supplier.Code, string.IsNullOrWhiteSpace(_supplier.TaxCode) ? "—" : _supplier.TaxCode);

        var items = AppState.FindQuotation(_supplier.Id)?.Items ?? new List<QuotationItem>();
        _rows.Clear();
        foreach (var it in items) _rows.Add(new ItemRow(it));

        // Bộ lọc loại gỗ
        var currentType = (FilterWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterWoodType.Items.Clear();
        FilterWoodType.Items.Add(new ComboBoxItem { Content = Lang.T("Quotations.Filter.AllTypes"), Tag = "ALL" });
        foreach (var t in items.Select(i => i.WoodType).Distinct())
            FilterWoodType.Items.Add(new ComboBoxItem { Content = t, Tag = t });
        SelectByTag(FilterWoodType, currentType);

        // Bộ lọc đơn vị tiền tệ
        if (FilterCurrency.Items.Count == 0)
        {
            FilterCurrency.Items.Add(new ComboBoxItem { Content = Lang.T("Quotations.Filter.AllCurrencies"), Tag = "ALL" });
            FilterCurrency.Items.Add(new ComboBoxItem { Content = "USD", Tag = "USD" });
            FilterCurrency.Items.Add(new ComboBoxItem { Content = "VND", Tag = "VND" });
            FilterCurrency.SelectedIndex = 0;
        }

        if (_view == null)
        {
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterPredicate;
            ItemGrid.ItemsSource = _view;
            ActionGrid.ItemsSource = _view;   // cột thao tác tách riêng, cùng nguồn dữ liệu
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
        var matchSearch = term.Length == 0
            || (r.WoodType ?? "").ToLowerInvariant().Contains(term)
            || (r.Grade ?? "").ToLowerInvariant().Contains(term)
            || (r.Origin ?? "").ToLowerInvariant().Contains(term)
            || (r.Specification ?? "").ToLowerInvariant().Contains(term);
        if (!matchSearch) return false;

        var currency = (FilterCurrency.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        if (currency != "ALL" && !string.Equals(r.PriceCurrency, currency, StringComparison.OrdinalIgnoreCase)) return false;

        bool Contains(string cellText, string filterBox) =>
            string.IsNullOrWhiteSpace(filterBox) ||
            (cellText ?? "").ToLowerInvariant().Contains(filterBox.Trim().ToLowerInvariant());

        return Contains(r.ThicknessText, FThicknessFilter.Text) && Contains(r.WidthText, FWidthColFilter.Text)
            && Contains(r.LengthText, FLengthColFilter.Text) && Contains(r.OriginText, FOriginFilter.Text)
            && Contains(r.Specification, FSpecFilter.Text) && Contains(r.PriceText, FPriceFilter.Text)
            && Contains(r.UpdatedAtText, FUpdatedFilter.Text);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        BtnClearColumnFilters.Visibility = AnyColumnFilterActive() ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    private void UpdateCountAndEmpty()
    {
        var n = _view.Cast<object>().Count();
        TotalCount.Text = n.ToString();
        EmptyRow.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- Bộ lọc theo từng cột ----------------

    private bool AnyColumnFilterActive() =>
        ((FilterWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL") != "ALL" ||
        ((FilterCurrency.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL") != "ALL" ||
        !string.IsNullOrWhiteSpace(FThicknessFilter.Text) || !string.IsNullOrWhiteSpace(FWidthColFilter.Text) ||
        !string.IsNullOrWhiteSpace(FLengthColFilter.Text) || !string.IsNullOrWhiteSpace(FOriginFilter.Text) ||
        !string.IsNullOrWhiteSpace(FSpecFilter.Text) || !string.IsNullOrWhiteSpace(FPriceFilter.Text) ||
        !string.IsNullOrWhiteSpace(FUpdatedFilter.Text);

    private void BtnToggleColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        var expand = ColumnFilterPanel.Visibility != Visibility.Visible;
        ColumnFilterPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        ToggleColumnFiltersLabel.Text = expand ? Lang.T("Common.HideColumnFilter") : Lang.T("Common.FilterByColumn");
    }

    private void BtnClearColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        FilterWoodType.SelectedIndex = 0;
        FilterCurrency.SelectedIndex = 0;
        FThicknessFilter.Text = "";
        FWidthColFilter.Text = "";
        FLengthColFilter.Text = "";
        FOriginFilter.Text = "";
        FSpecFilter.Text = "";
        FPriceFilter.Text = "";
        FUpdatedFilter.Text = "";
        BtnClearColumnFilters.Visibility = Visibility.Collapsed;
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    // ---------------- Cảnh báo inline ----------------

    private static void ShowWarn(TextBlock w, string msg) { w.Text = msg; w.Visibility = Visibility.Visible; }

    private void ClearWarnings() =>
        WThickRange.Visibility = WWidthRange.Visibility = WLengthRange.Visibility
            = WPrice.Visibility = WSubType.Visibility = Visibility.Collapsed;

    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TextBlock w) w.Visibility = Visibility.Collapsed;
        if (sender == FThickMin || sender == FThickMax) ApplyThicknessVisibility();
    }

    /// <summary>Đọc chip đang chọn của 1 bộ 3 radio Đơn lẻ/Khoảng/Nhiều — mặc định Khoảng nếu chưa chip nào được chọn.</summary>
    private static DimMode GetMode(RadioButton single, RadioButton range, RadioButton multi) =>
        multi.IsChecked == true ? DimMode.List : single.IsChecked == true ? DimMode.Single : DimMode.Range;

    private static void SetMode(RadioButton single, RadioButton range, RadioButton multi, DimMode mode) =>
        (mode == DimMode.Single ? single : mode == DimMode.List ? multi : range).IsChecked = true;

    /// <summary>Suy ra chế độ ban đầu từ dữ liệu đã lưu (không cần cột DB riêng): có danh sách giá trị → Nhiều;
    /// Từ = Đến (cùng 1 số) → Đơn lẻ; còn lại (kể cả khoảng mở 1 phía hoặc để trống) → Khoảng.</summary>
    private static DimMode DetermineMode(string valuesRaw, double? min, double? max) =>
        !string.IsNullOrWhiteSpace(valuesRaw) ? DimMode.List
        : min.HasValue && max.HasValue && Math.Abs(min.Value - max.Value) < 0.0001 ? DimMode.Single
        : DimMode.Range;

    /// <summary>Hiện/ẩn ô "Đến" theo chế độ đang chọn: Khoảng = hiện đủ 2 ô; Đơn lẻ/Nhiều = chỉ 1 ô (Từ giãn hết chỗ).
    /// Tooltip hướng dẫn cú pháp "/" chỉ hiện khi đang ở chế độ Nhiều.</summary>
    private void ApplyDimVisibility(TextBox minBox, TextBox maxBox, TextBlock sep, DimMode mode)
    {
        var showMax = mode == DimMode.Range;
        maxBox.Visibility = showMax ? Visibility.Visible : Visibility.Collapsed;
        sep.Visibility = showMax ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(minBox, showMax ? 1 : 3);
        minBox.ToolTip = mode == DimMode.List ? Lang.T("Quotations.Field.RangeHint") : null;
    }

    private void ThicknessMode_Changed(object sender, RoutedEventArgs e) => ApplyThicknessVisibility();

    private void WidthMode_Changed(object sender, RoutedEventArgs e) =>
        ApplyDimVisibility(FWidthMin, FWidthMax, FWidthSep, GetMode(FWidthModeSingle, FWidthModeRange, FWidthModeMulti));

    private void LengthMode_Changed(object sender, RoutedEventArgs e) =>
        ApplyDimVisibility(FLengthMin, FLengthMax, FLengthSep, GetMode(FLengthModeSingle, FLengthModeRange, FLengthModeMulti));

    /// <summary>Gỗ nhóm Footage — mm chính xác vô nghĩa, độ dày chỉ mô tả theo ký hiệu ngành gỗ Mỹ (vd "4/4\"").</summary>
    private static bool IsFootage(string woodType) => AppState.GetVolumeRule(woodType) == VolumeRule.ByFootage;

    /// <summary>Như <see cref="ApplyDimVisibility"/> nhưng cho Dày — riêng field này Footage vẫn dùng ký hiệu
    /// inch (không phải mm) nên hiện thêm placeholder hint "vd 4/4&quot;"/"vd 8/4&quot;" khi ô trống; chip
    /// "Nhiều" không có ý nghĩa với Footage (đã ẩn ở <see cref="SyncThicknessForWoodType"/>) vì "/" đã dùng
    /// cho ký hiệu phân số inch.</summary>
    private void ApplyThicknessVisibility()
    {
        var footage = IsFootage((FWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "");
        var mode = GetMode(FThickModeSingle, FThickModeRange, FThickModeMulti);
        ApplyDimVisibility(FThickMin, FThickMax, FThickSep, mode);
        FThickMinHint.Visibility = footage && string.IsNullOrEmpty(FThickMin.Text) ? Visibility.Visible : Visibility.Collapsed;
        FThickMaxHint.Visibility = footage && mode == DimMode.Range && string.IsNullOrEmpty(FThickMax.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Đổi nhãn Dày (mm thường vs ký hiệu inch Footage) + ẩn chip "Nhiều" khi gỗ Footage (không hỗ trợ
    /// danh sách rời rạc ở Dày vì "/" đã dùng cho ký hiệu phân số inch) — gọi lại mỗi khi đổi Loại gỗ.</summary>
    private void SyncThicknessForWoodType()
    {
        var woodType = (FWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var footage = IsFootage(woodType);
        LblThickness.Text = footage ? Lang.T("Quotations.Field.Thickness") : Lang.T("Quotations.Field.ThicknessMm");
        FThickModeMulti.Visibility = footage ? Visibility.Collapsed : Visibility.Visible;
        if (footage && FThickModeMulti.IsChecked == true) FThickModeRange.IsChecked = true;
        ApplyThicknessVisibility();
    }

    // ---------------- Thêm / Xem / Sửa ----------------

    private static double D(string s) => Fmt.ParseNum(s);

    private static string NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Parse 1 cặp Từ/Đến: trống = null (không giới hạn); validate số hợp lệ và Từ &lt;= Đến.</summary>
    private static bool ValidateRange(string minText, string maxText, TextBlock warn, string label, out double? min, out double? max)
    {
        var minOk = TryParseOptional(minText, out min);
        var maxOk = TryParseOptional(maxText, out max);
        if (!minOk || !maxOk) { ShowWarn(warn, Lang.T("Quotations.Warn.RangeInvalid", label)); return false; }
        if (min != null && max != null && min > max) { ShowWarn(warn, Lang.T("Quotations.Warn.RangeOrder", label)); return false; }
        return true;
    }

    /// <summary>
    /// Validate 1 field kích thước theo chip Đơn lẻ/Khoảng/Nhiều đang chọn (xem <see cref="DimMode"/>):
    /// Đơn lẻ = 1 số (lưu Min=Max=chính nó, khớp giá CHÍNH XÁC bằng số đó); Khoảng = như <see cref="ValidateRange"/>
    /// (Từ/Đến, 1 hoặc cả 2 có thể để trống); Nhiều = danh sách rời rạc cách nhau "/" (vd "1220/2440/3000"),
    /// khớp giá khi giá trị thực tế bằng CHÍNH XÁC 1 trong các số này (không phải một khoảng liên tục).
    /// </summary>
    private static bool ValidateDimension(DimMode mode, string minText, string maxText, TextBlock warn, string label,
        out double? min, out double? max, out string valuesRaw)
    {
        valuesRaw = null;
        if (mode == DimMode.List)
        {
            min = max = null;
            var list = Fmt.ParseValueList(minText);
            if (!string.IsNullOrWhiteSpace(minText) && list.Count == 0) { ShowWarn(warn, Lang.T("Quotations.Warn.RangeInvalid", label)); return false; }
            if (list.Count > 0) valuesRaw = string.Join("/", list.Select(Fmt.Num));
            return true;
        }
        if (mode == DimMode.Single) return ValidateRange(minText, minText, warn, label, out min, out max);
        return ValidateRange(minText, maxText, warn, label, out min, out max);
    }

    /// <summary>
    /// Như <see cref="ValidateRange"/> nhưng cho độ dày gỗ Footage: chấp nhận ký hiệu inch (4/4", 1"...)
    /// thay vì chỉ số mm. Parse ra mm để khớp giá (qua <see cref="WoodVolumeCalculator.ParseFootageThicknessMm"/>)
    /// nhưng vẫn giữ nguyên văn ký hiệu gốc để hiển thị lại. Gọi với (minText, minText) cho chế độ Đơn lẻ
    /// (Min=Max=cùng 1 ký hiệu, khớp giá CHÍNH XÁC — xem cách dùng ở <see cref="BtnSave_Click"/>).
    /// </summary>
    private static bool ValidateFootageThickness(string minText, string maxText, TextBlock warn,
        out double? min, out double? max, out string minNote, out string maxNote)
    {
        minText = (minText ?? "").Trim();
        maxText = (maxText ?? "").Trim();
        minNote = minText.Length == 0 ? null : minText;
        maxNote = maxText.Length == 0 ? null : maxText;
        min = minNote == null ? null : WoodVolumeCalculator.ParseFootageThicknessMm(minText);
        max = maxNote == null ? null : WoodVolumeCalculator.ParseFootageThicknessMm(maxText);
        if ((minNote != null && min == 0) || (maxNote != null && max == 0))
        {
            ShowWarn(warn, Lang.T("Quotations.Warn.ThicknessInvalidNote"));
            return false;
        }
        if (min != null && max != null && min > max)
        {
            ShowWarn(warn, Lang.T("Quotations.Warn.ThicknessOrder"));
            return false;
        }
        return true;
    }

    private static bool TryParseOptional(string text, out double? value)
    {
        if (string.IsNullOrWhiteSpace(text)) { value = null; return true; }
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("vi-VN"), out var v) && v >= 0)
        {
            value = v;
            return true;
        }
        value = null;
        return false;
    }

    private void SetReadOnly(bool ro)
    {
        FOrigin.IsReadOnly = FSpec.IsReadOnly = FPrice.IsReadOnly = ro;
        FThickMin.IsReadOnly = FThickMax.IsReadOnly = FWidthMin.IsReadOnly = FWidthMax.IsReadOnly = FLengthMin.IsReadOnly = FLengthMax.IsReadOnly = ro;
        var bg = ro ? (Brush)FindResource("Slate50") : Brushes.White;
        FOrigin.Background = FSpec.Background = bg;
        FThickMin.Background = FThickMax.Background = FWidthMin.Background = FWidthMax.Background = FLengthMin.Background = FLengthMax.Background = bg;
        PriceInputBorder.Background = bg;
        FPriceCurrency.IsEnabled = !ro;
        FWoodType.IsEnabled = FWoodSubType.IsEnabled = !ro;
        // Chip Đơn lẻ/Khoảng/Nhiều không được đổi khi đang ở view mode (chỉ đọc).
        FThickModeSingle.IsEnabled = FThickModeRange.IsEnabled = FThickModeMulti.IsEnabled = !ro;
        FWidthModeSingle.IsEnabled = FWidthModeRange.IsEnabled = FWidthModeMulti.IsEnabled = !ro;
        FLengthModeSingle.IsEnabled = FLengthModeRange.IsEnabled = FLengthModeMulti.IsEnabled = !ro;
    }

    private void PriceInput_GotFocus(object sender, RoutedEventArgs e) =>
        PriceInputBorder.BorderBrush = (Brush)FindResource("Blue500");

    private void PriceInput_LostFocus(object sender, RoutedEventArgs e) =>
        PriceInputBorder.BorderBrush = (Brush)FindResource("Slate200");

    private void EnterAddMode()
    {
        _mode = "add";
        _editingId = null;
        ClearWarnings();
        SetReadOnly(false);
        FormTitle.Text = Lang.T("Quotations.Form.AddTitle");
        FormSaveBtn.Content = Lang.T("Quotations.SaveButton");
        FormCancelBtn.Content = Lang.T("Common.Cancel");
        if (FWoodType.Items.Count > 0) FWoodType.SelectedIndex = 0;
        PopulateSubCombo((FWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "");
        FOrigin.Text = FSpec.Text = FPrice.Text = "";
        FThickMin.Text = FThickMax.Text = FWidthMin.Text = FWidthMax.Text = FLengthMin.Text = FLengthMax.Text = "";
        FPriceCurrency.SelectedIndex = 0;   // mặc định USD
        SetMode(FThickModeSingle, FThickModeRange, FThickModeMulti, DimMode.Single);
        SetMode(FWidthModeSingle, FWidthModeRange, FWidthModeMulti, DimMode.Single);
        SetMode(FLengthModeSingle, FLengthModeRange, FLengthModeMulti, DimMode.Single);
        SyncThicknessForWoodType();
    }

    private static string NumOrBlank(double? v) => v.HasValue ? Fmt.Num(v.Value) : "";

    /// <summary>Suy ra chip Đơn lẻ/Khoảng cho Dày gỗ Footage từ 2 ký hiệu ghi chú đã lưu (không có cột DB
    /// riêng lưu chế độ): MinNote = MaxNote (cùng 1 ký hiệu, khác null) → Đơn lẻ; còn lại (kể cả 1 phía mở
    /// hoặc để trống) → Khoảng. Footage không hỗ trợ chế độ Nhiều (xem <see cref="SyncThicknessForWoodType"/>).</summary>
    private static DimMode DetermineFootageMode(string minNote, string maxNote) =>
        minNote != null && maxNote != null && minNote == maxNote ? DimMode.Single : DimMode.Range;

    private void FillForm(QuotationItem it)
    {
        var footage = IsFootage(it.WoodType);
        SelectByTag(FWoodType, it.WoodType);
        PopulateSubCombo(it.WoodType, it.WoodSubType);
        FOrigin.Text = it.Origin;
        FThickMin.Text = footage ? (it.ThicknessMinNote ?? NumOrBlank(it.ThicknessMin))
            : (!string.IsNullOrWhiteSpace(it.ThicknessValues) ? it.ThicknessValues : NumOrBlank(it.ThicknessMin));
        FThickMax.Text = footage ? (it.ThicknessMaxNote ?? NumOrBlank(it.ThicknessMax))
            : (!string.IsNullOrWhiteSpace(it.ThicknessValues) ? "" : NumOrBlank(it.ThicknessMax));
        FWidthMin.Text = !string.IsNullOrWhiteSpace(it.WidthValues) ? it.WidthValues : NumOrBlank(it.WidthMin);
        FWidthMax.Text = !string.IsNullOrWhiteSpace(it.WidthValues) ? "" : NumOrBlank(it.WidthMax);
        FLengthMin.Text = !string.IsNullOrWhiteSpace(it.LengthValues) ? it.LengthValues : NumOrBlank(it.LengthMin);
        FLengthMax.Text = !string.IsNullOrWhiteSpace(it.LengthValues) ? "" : NumOrBlank(it.LengthMax);
        FPrice.Text = Fmt.Num((double)it.Price);
        FPriceCurrency.SelectedIndex = string.Equals(it.PriceCurrency, "VND", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        FSpec.Text = it.Specification;
        SyncThicknessForWoodType();

        // Suy ra chip Đơn lẻ/Khoảng/Nhiều từ dữ liệu đã lưu (không có cột DB riêng lưu chế độ).
        SetMode(FThickModeSingle, FThickModeRange, FThickModeMulti, footage
            ? DetermineFootageMode(it.ThicknessMinNote, it.ThicknessMaxNote)
            : DetermineMode(it.ThicknessValues, it.ThicknessMin, it.ThicknessMax));
        SetMode(FWidthModeSingle, FWidthModeRange, FWidthModeMulti, DetermineMode(it.WidthValues, it.WidthMin, it.WidthMax));
        SetMode(FLengthModeSingle, FLengthModeRange, FLengthModeMulti, DetermineMode(it.LengthValues, it.LengthMin, it.LengthMax));
    }

    private void EnterViewMode(QuotationItem it)
    {
        _mode = "view";
        _editingId = it.Id;
        ClearWarnings();
        FillForm(it);
        SetReadOnly(true);
        FormTitle.Text = Lang.T("Quotations.Form.ViewTitle", it.WoodType, new ItemRow(it).ThicknessText);
        FormSaveBtn.Content = Lang.T("Common.Edit");
        FormCancelBtn.Content = Lang.T("Common.Close");
        AddFormPanel.Visibility = Visibility.Visible;
    }

    private void EnterEditMode()
    {
        _mode = "edit";
        ClearWarnings();
        SetReadOnly(false);
        FormTitle.Text = Lang.T("Quotations.Form.EditTitle");
        FormSaveBtn.Content = Lang.T("Common.Update");
        FormCancelBtn.Content = Lang.T("Common.CancelEdit");
        FWoodSubType.Focus();
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
        // Đang sửa → xác nhận hủy, bỏ thay đổi và quay lại xem chi tiết (không lưu)
        if (_mode == "edit")
        {
            if (!ConfirmDiscard(Lang.T("Common.Confirm.DiscardEdit"))) return;
            var it = AppState.FindQuotation(_supplier.Id)?.Items.FirstOrDefault(i => i.Id == _editingId);
            if (it != null) { EnterViewMode(it); return; }
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
        MessageBox.Show(message, Lang.T("Common.ConfirmDiscardTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == "view") { EnterEditMode(); return; }

        var woodType = (FWoodType.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var footage = IsFootage(woodType);

        ClearWarnings();
        var ok = true;
        var thickMode = GetMode(FThickModeSingle, FThickModeRange, FThickModeMulti);
        string thickMinNote = null, thickMaxNote = null, thickValues = null;
        double? thickMin, thickMax;
        if (footage)
        {
            // Đơn lẻ = Min/Max cùng 1 ký hiệu (khớp giá CHÍNH XÁC); Khoảng = như trước (2 ký hiệu, 1 phía có thể để trống).
            var maxTextForValidate = thickMode == DimMode.Single ? FThickMin.Text : FThickMax.Text;
            if (!ValidateFootageThickness(FThickMin.Text, maxTextForValidate, WThickRange, out thickMin, out thickMax, out thickMinNote, out thickMaxNote)) ok = false;
        }
        else
        {
            if (!ValidateDimension(thickMode, FThickMin.Text, FThickMax.Text, WThickRange, Lang.T("Quotations.Label.Thickness"), out thickMin, out thickMax, out thickValues)) ok = false;
        }
        var widthMode = GetMode(FWidthModeSingle, FWidthModeRange, FWidthModeMulti);
        var lengthMode = GetMode(FLengthModeSingle, FLengthModeRange, FLengthModeMulti);
        if (!ValidateDimension(widthMode, FWidthMin.Text, FWidthMax.Text, WWidthRange, Lang.T("Quotations.Label.Width"), out var widthMin, out var widthMax, out var widthValues)) ok = false;
        if (!ValidateDimension(lengthMode, FLengthMin.Text, FLengthMax.Text, WLengthRange, Lang.T("Quotations.Label.Length"), out var lengthMin, out var lengthMax, out var lengthValues)) ok = false;
        if (D(FPrice.Text) <= 0) { ShowWarn(WPrice, Lang.T("Quotations.Warn.PriceRequired")); ok = false; }
        if (!ok) return;

        // Báo giá: để trống phân loại con = áp cho MỌI con của loại cha (fallback cấp cha) — không bắt buộc.
        var item = new QuotationItem
        {
            Id = _editingId,
            WoodType = woodType,
            WoodSubType = NullIfBlank((FWoodSubType.SelectedItem as ComboBoxItem)?.Tag as string),
            Grade = null,
            ThicknessMin = thickMin,
            ThicknessMax = thickMax,
            ThicknessMinNote = thickMinNote,
            ThicknessMaxNote = thickMaxNote,
            ThicknessValues = thickValues,
            WidthMin = widthMin,
            WidthMax = widthMax,
            WidthValues = widthValues,
            LengthMin = lengthMin,
            LengthMax = lengthMax,
            LengthValues = lengthValues,
            Origin = NullIfBlank(FOrigin.Text),
            Specification = NullIfBlank(FSpec.Text),
            Price = (decimal)D(FPrice.Text),
            PriceCurrency = (FPriceCurrency.SelectedItem as ComboBoxItem)?.Tag as string ?? "USD"
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
            MessageBox.Show(Flatten(ex), Lang.T("Common.CannotSaveTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Gộp message của toàn bộ chuỗi InnerException để lộ nguyên nhân gốc (vd lỗi SQLite).</summary>
    private static string Flatten(Exception ex)
    {
        var msgs = new List<string>();
        for (var e = ex; e != null; e = e.InnerException) msgs.Add(e.Message);
        return string.Join("\n→ ", msgs);
    }

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ItemRow r) EnterViewMode(r.Item);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ItemRow r) return;
        var confirm = MessageBox.Show(Lang.T("Quotations.Confirm.Delete", r.WoodType, r.ThicknessText),
            Lang.T("Common.AppTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        AppState.DeleteQuotationItem(r.Item.Id);
        if (_editingId == r.Item.Id) { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) => _back?.Invoke();
}

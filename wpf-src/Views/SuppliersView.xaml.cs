using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WoodInventory.Data;
using WoodInventory.Domain;

namespace WoodInventory.Views;

public partial class SuppliersView : UserControl, IModuleView
{
    /// <summary>Dòng dữ liệu cho DataGrid.</summary>
    public sealed class SupRow
    {
        public Supplier Supplier { get; }
        public string Name => Supplier.Name;
        public string CodeLabel => $"Tên gọi tắt: {Supplier.Code}";
        public string TaxCode => string.IsNullOrWhiteSpace(Supplier.TaxCode) ? "—" : Supplier.TaxCode;
        public string Address => string.IsNullOrWhiteSpace(Supplier.Address) ? "—" : Supplier.Address;
        public SupRow(Supplier s) => Supplier = s;
    }

    // null = đang thêm mới; có giá trị = đang xem/sửa NCC có Id này
    private string _editingId;
    private string _mode = "add";   // add | view | edit
    private readonly List<SupRow> _rows = new();
    private ICollectionView _view;

    private QuotationDetailView _detailView;
    private Supplier _detailSupplier;
    private MainWindow Main => Window.GetWindow(this) as MainWindow;

    public SuppliersView()
    {
        InitializeComponent();
        RefreshView();
        Helpers.GridLayoutStore.Attach(Grid, "suppliers");
        Helpers.GridPairSync.Link(Grid, ActionGrid);
    }

    public void RefreshView()
    {
        if (_detailView != null && _detailSupplier != null)
        {
            // Đang ở trang báo giá của 1 NCC → cập nhật nó + giữ breadcrumb
            _detailView.RefreshView();
            Main?.SetBreadcrumbDetail(_detailSupplier.Name, BackToList);
            return;
        }

        _rows.Clear();
        foreach (var s in AppState.Suppliers) _rows.Add(new SupRow(s));

        if (_view == null)
        {
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterPredicate;
            Grid.ItemsSource = _view;
            ActionGrid.ItemsSource = _view;   // cột thao tác tách riêng, cùng nguồn
        }
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    private bool FilterPredicate(object o)
    {
        var r = (SupRow)o;
        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        var matchSearch = term.Length == 0
            || (r.Supplier.Name ?? "").ToLowerInvariant().Contains(term)
            || (r.Supplier.Code ?? "").ToLowerInvariant().Contains(term)
            || (r.Supplier.TaxCode ?? "").ToLowerInvariant().Contains(term)
            || (r.Supplier.Address ?? "").ToLowerInvariant().Contains(term);

        bool Contains(string cellText, string filterBox) =>
            string.IsNullOrWhiteSpace(filterBox) ||
            (cellText ?? "").ToLowerInvariant().Contains(filterBox.Trim().ToLowerInvariant());

        var matchColumns =
            Contains(r.Name, FNameFilter.Text) &&
            Contains(r.TaxCode, FTaxCodeFilter.Text) &&
            Contains(r.Address, FAddressFilter.Text);

        return matchSearch && matchColumns;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        BtnClearColumnFilters.Visibility = AnyColumnFilterActive() ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    // ---------------- Bộ lọc theo từng cột ----------------

    private bool AnyColumnFilterActive() =>
        !string.IsNullOrWhiteSpace(FNameFilter.Text) || !string.IsNullOrWhiteSpace(FTaxCodeFilter.Text) ||
        !string.IsNullOrWhiteSpace(FAddressFilter.Text);

    private void BtnToggleColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        var expand = ColumnFilterPanel.Visibility != Visibility.Visible;
        ColumnFilterPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        ToggleColumnFiltersLabel.Text = expand ? "Ẩn lọc theo cột" : "Lọc theo cột";
    }

    private void BtnClearColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        FNameFilter.Text = "";
        FTaxCodeFilter.Text = "";
        FAddressFilter.Text = "";
        BtnClearColumnFilters.Visibility = Visibility.Collapsed;
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    // ---------------- Điều hướng sang trang báo giá của NCC (trong cùng tab) ----------------

    private void OpenQuotation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SupRow r) OpenDetail(r.Supplier);
    }

    private void OpenDetail(Supplier s)
    {
        // Đóng form thêm/sửa NCC (nếu đang mở) trước khi vào trang báo giá
        AddFormPanel.Visibility = Visibility.Collapsed;
        EnterAddMode();

        _detailSupplier = s;
        _detailView = new QuotationDetailView(s, BackToList);
        DetailHost.Content = _detailView;
        ListRoot.Visibility = Visibility.Collapsed;
        DetailHost.Visibility = Visibility.Visible;
        Main?.SetBreadcrumbDetail(s.Name, BackToList);
    }

    private void BackToList()
    {
        _detailView = null;
        _detailSupplier = null;
        DetailHost.Content = null;
        DetailHost.Visibility = Visibility.Collapsed;
        ListRoot.Visibility = Visibility.Visible;
        Main?.SetBreadcrumbDetail(null);
        RefreshView();
    }

    private void UpdateCountAndEmpty()
    {
        var n = _view.Cast<object>().Count();
        TotalCount.Text = n.ToString();
        EmptyRow.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SupRow r) EnterViewMode(r.Supplier);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SupRow r) DeleteSupplier(r.Supplier);
    }

    // ---------------- Thêm / Sửa ----------------

    /// <summary>Bật/tắt chế độ chỉ-đọc cho toàn bộ ô nhập trong form.</summary>
    private void SetReadOnly(bool ro)
    {
        foreach (var box in new[] { FName, FCode, FTaxCode, FAddress, FPhone, FBankAccount })
        {
            box.IsReadOnly = ro;
            box.Background = ro ? (Brush)FindResource("Slate50") : Brushes.White;
        }
    }

    private void EnterAddMode()
    {
        _mode = "add";
        _editingId = null;
        ClearWarnings();
        SetReadOnly(false);
        FormTitle.Text = "Khai Báo Nhà Cung Cấp Mới";
        FormSaveBtn.Content = "Lưu nhà cung cấp";
        FormCancelBtn.Content = "Hủy bỏ";
        FName.Text = FCode.Text = FTaxCode.Text = FAddress.Text = FPhone.Text = FBankAccount.Text = "";
    }

    /// <summary>Xem chi tiết: đẩy form lên như edit nhưng chỉ-đọc, nút thành "Chỉnh sửa".</summary>
    private void EnterViewMode(Supplier s)
    {
        _mode = "view";
        _editingId = s.Id;
        FName.Text = s.Name;
        FCode.Text = s.Code;
        FTaxCode.Text = s.TaxCode;
        FAddress.Text = s.Address;
        FPhone.Text = s.Phone;
        FBankAccount.Text = s.BankAccount;
        SetReadOnly(true);
        FormTitle.Text = $"Chi Tiết Nhà Cung Cấp — {s.Name}";
        FormSaveBtn.Content = "Chỉnh sửa";
        FormCancelBtn.Content = "Đóng";
        AddFormPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Chuyển từ xem sang sửa: mở khóa ô nhập, nút thành "Cập nhật".</summary>
    private void EnterEditMode()
    {
        _mode = "edit";
        ClearWarnings();
        SetReadOnly(false);
        FormTitle.Text = $"Sửa Nhà Cung Cấp — {FName.Text}";
        FormSaveBtn.Content = "Cập nhật";
        FormCancelBtn.Content = "Hủy sửa";
        FName.Focus();
        FName.SelectAll();
    }

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        // Đang mở sẵn ở chế độ thêm mới → bấm lần nữa thì đóng
        if (AddFormPanel.Visibility == Visibility.Visible && _mode == "add")
        {
            AddFormPanel.Visibility = Visibility.Collapsed;
            return;
        }
        // Còn lại (đang ẩn, hoặc đang xem/sửa) → chuyển thẳng sang thêm mới, xóa nội dung cũ
        EnterAddMode();
        AddFormPanel.Visibility = Visibility.Visible;
    }

    // ---------------- Cảnh báo inline ----------------

    private static void ShowWarn(TextBlock w, string msg)
    {
        w.Text = msg;
        w.Visibility = Visibility.Visible;
    }

    private void ClearWarnings() =>
        WName.Visibility = WCode.Visibility = WTaxCode.Visibility = Visibility.Collapsed;

    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TextBlock w) w.Visibility = Visibility.Collapsed;
    }

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
    {
        // Đang sửa → xác nhận hủy, bỏ thay đổi và quay lại xem chi tiết (không lưu)
        if (_mode == "edit")
        {
            if (!ConfirmDiscard("Những thay đổi sẽ không được lưu, tiếp tục huỷ?")) return;
            var s = AppState.FindSupplier(_editingId);
            if (s != null) { EnterViewMode(s); return; }
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

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // Đang xem chi tiết → bấm "Chỉnh sửa" thì chuyển sang chế độ sửa
        if (_mode == "view") { EnterEditMode(); return; }

        ClearWarnings();
        var ok = true;
        if (string.IsNullOrWhiteSpace(FName.Text)) { ShowWarn(WName, "Vui lòng nhập tên nhà cung cấp."); ok = false; }
        if (string.IsNullOrWhiteSpace(FCode.Text)) { ShowWarn(WCode, "Vui lòng nhập tên gọi tắt."); ok = false; }
        if (string.IsNullOrWhiteSpace(FTaxCode.Text)) { ShowWarn(WTaxCode, "Vui lòng nhập mã số thuế."); ok = false; }
        if (!ok) return;

        var supplier = new Supplier
        {
            Id = _editingId ?? $"SUP-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Name = FName.Text,
            Code = FCode.Text,
            TaxCode = FTaxCode.Text,
            Address = FAddress.Text,
            Phone = FPhone.Text,
            BankAccount = FBankAccount.Text
        };

        try
        {
            if (_editingId == null) AppState.AddSupplier(supplier);
            else AppState.UpdateSupplier(supplier);

            AddFormPanel.Visibility = Visibility.Collapsed;
            EnterAddMode();
        }
        catch (Exception ex)
        {
            ShowWarn(WCode, ex.Message);   // vd trùng tên gọi tắt → cảnh báo ngay dưới field
        }
    }

    private void DeleteSupplier(Supplier s)
    {
        var confirm = MessageBox.Show($"Bạn có chắc muốn xóa nhà cung cấp \"{s.Name}\"?",
            "Quản Lý Gỗ", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            AppState.DeleteSupplier(s.Id);
            if (_editingId == s.Id) { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

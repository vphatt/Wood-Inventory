using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WoodInventory.Data;
using WoodInventory.Domain;
using WoodInventory.Helpers;

namespace WoodInventory.Views;

/// <summary>
/// Trang danh sách báo giá: mỗi NCC một dòng (số mục giá + ngày áp dụng).
/// Bấm "Xem báo giá" điều hướng sang <see cref="QuotationDetailView"/> ngay trong tab
/// (breadcrumb: Báo Giá Gỗ / Tên NCC).
/// </summary>
public partial class QuotationsView : UserControl, IModuleView
{
    public sealed class QuoRow
    {
        public Supplier Supplier { get; }
        public string Name => Supplier.Name;
        public string CodeLabel => $"Tên gọi tắt: {Supplier.Code}";
        public int ItemCount { get; }
        public string ItemCountText => $"{ItemCount} mục";
        public DateTime? Date { get; }
        public string DateText => Date.HasValue ? Fmt.Date(Date.Value) : "—";
        public bool HasQuotation { get; }
        public QuoRow(Supplier s)
        {
            Supplier = s;
            var q = AppState.FindQuotation(s.Id);
            HasQuotation = q != null;
            ItemCount = q?.Items.Count ?? 0;
            Date = q?.EffectiveDate;
        }
    }

    private readonly List<QuoRow> _rows = new();
    private ICollectionView _view;
    private QuotationDetailView _detailView;
    private Supplier _detailSupplier;

    private MainWindow Main => Window.GetWindow(this) as MainWindow;

    public QuotationsView()
    {
        InitializeComponent();
        RebuildList();
        GridLayoutStore.Attach(SupplierGrid, "quotation-suppliers");
    }

    public void RefreshView()
    {
        if (_detailView != null && _detailSupplier != null)
        {
            // Đang ở trang chi tiết → cập nhật nó + giữ breadcrumb khi quay lại tab
            _detailView.RefreshView();
            Main?.SetBreadcrumbDetail(_detailSupplier.Name, BackToList);
            return;
        }
        RebuildList();
    }

    private void RebuildList()
    {
        _rows.Clear();
        foreach (var s in AppState.Suppliers) _rows.Add(new QuoRow(s));

        if (_view == null)
        {
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterPredicate;
            SupplierGrid.ItemsSource = _view;
            ActionGrid.ItemsSource = _view;   // cột thao tác tách riêng, cùng nguồn
        }
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    private bool FilterPredicate(object o)
    {
        var r = (QuoRow)o;
        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        if (term.Length == 0) return true;
        return (r.Supplier.Name ?? "").ToLowerInvariant().Contains(term)
            || (r.Supplier.Code ?? "").ToLowerInvariant().Contains(term);
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

    // ---------------- Điều hướng list <-> detail ----------------

    private void OpenDetail(Supplier s)
    {
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
        RebuildList();
    }

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is QuoRow r) OpenDetail(r.Supplier);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not QuoRow r) return;
        if (!r.HasQuotation)
        {
            MessageBox.Show("Nhà cung cấp này chưa có mục giá nào.", "Quản Lý Gỗ",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var confirm = MessageBox.Show($"Xóa toàn bộ báo giá ({r.ItemCount} mục) của \"{r.Name}\"?",
            "Quản Lý Gỗ", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        AppState.DeleteQuotation(r.Supplier.Id);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WoodInventory.Helpers;

/// <summary>
/// Đồng bộ HOVER + LỰA CHỌN giữa 2 DataGrid tách riêng cùng hiển thị 1 danh sách (bảng nội dung + cột
/// "THAO TÁC" ghim phải, dùng khắp app: Receipts, Lots, Quotations, Suppliers, WoodCategories,
/// WoodSubCategories...). Vì là 2 control độc lập, rê/chọn dòng ở bảng này không tự làm dòng tương ứng ở
/// bảng kia sáng theo (và ngược lại) — gây cảm giác "tô sai dòng". Gọi <see cref="Link"/> 1 lần (constructor
/// View, sau InitializeComponent) để nối 2 DataGrid đó; bảng không tách cột thao tác thì không cần gọi gì
/// thêm, AutoHover đã tự lo hover cho chính nó.
/// Lựa chọn: cố tình đồng bộ bằng cách chép thẳng SelectedItem sang grid kia (KHÔNG dùng
/// IsSynchronizedWithCurrentItem) — thuộc tính đó ràng 2 grid vào chung CurrentItem của ICollectionView,
/// và mỗi lần View gọi `_view.Refresh()` (lọc/tìm kiếm, load lại dữ liệu...) WPF tự đưa CurrentItem về
/// dòng đầu tiên, kéo theo cả 2 grid bị ép chọn lại dòng đầu — từng gây bug "tự mở chi tiết dòng đầu,
/// bấm Đóng không tắt được" ở LotsView (RebuildRows gọi Refresh() rồi mới set lại SelectedItem đúng ý,
/// nhưng CurrentItem-sync đã lỡ bắn SelectionChanged chọn dòng đầu trước đó, ghi đè ý định).
/// </summary>
public static class GridPairSync
{
    public static readonly DependencyProperty AutoHoverProperty =
        DependencyProperty.RegisterAttached("AutoHover", typeof(bool), typeof(GridPairSync),
            new PropertyMetadata(false, OnAutoHoverChanged));

    public static void SetAutoHover(DataGrid grid, bool value) => grid.SetValue(AutoHoverProperty, value);
    public static bool GetAutoHover(DataGrid grid) => (bool)grid.GetValue(AutoHoverProperty);

    private static readonly DependencyProperty PartnerProperty =
        DependencyProperty.RegisterAttached("Partner", typeof(DataGrid), typeof(GridPairSync), new PropertyMetadata(null));

    private static readonly DependencyProperty WiredProperty =
        DependencyProperty.RegisterAttached("Wired", typeof(bool), typeof(GridPairSync), new PropertyMetadata(false));

    private static readonly DependencyProperty SyncingProperty =
        DependencyProperty.RegisterAttached("Syncing", typeof(bool), typeof(GridPairSync), new PropertyMetadata(false));

    private static readonly DependencyProperty SyncingScrollProperty =
        DependencyProperty.RegisterAttached("SyncingScroll", typeof(bool), typeof(GridPairSync), new PropertyMetadata(false));

    /// <summary>Nối 2 DataGrid tách riêng (nội dung + thao tác) — gọi 1 lần trong constructor View.</summary>
    public static void Link(DataGrid a, DataGrid b)
    {
        a.SetValue(PartnerProperty, b);
        b.SetValue(PartnerProperty, a);
        WireSelection(a);
        WireSelection(b);
        WireScroll(a);
        WireScroll(b);
    }

    /// <summary>
    /// Đồng bộ VỊ TRÍ CUỘN DỌC giữa 2 DataGrid — cần từ khi mỗi bảng bị giới hạn chiều cao viewport
    /// (~14 dòng, xem <c>MaxHeight</c> trong style <c>DataTable</c>) nên tự cuộn nội bộ thay vì để trang
    /// ngoài cuộn: cuộn ở ItemGrid (thanh cuộn thật, hiện) phải kéo theo ActionGrid (thanh cuộn ẩn —
    /// <c>ScrollViewer.VerticalScrollBarVisibility="Hidden"</c>, KHÔNG phải "Disabled" vì Disabled chặn
    /// luôn khả năng cuộn bằng API) đi theo cùng offset, không thì cột "Thao tác" lệch hàng khi cuộn.
    /// ScrollViewer nội bộ của DataGrid chỉ có sau khi template áp dụng nên phải đợi <c>Loaded</c>.
    /// </summary>
    private static void WireScroll(DataGrid grid)
    {
        void Hook()
        {
            if (FindScrollViewer(grid) is not ScrollViewer sv) return;
            sv.ScrollChanged += (_, e) =>
            {
                if (e.VerticalChange == 0) return;
                if ((bool)grid.GetValue(SyncingScrollProperty)) return;
                if (grid.GetValue(PartnerProperty) is not DataGrid partner) return;
                var offset = sv.VerticalOffset;
                // Hoãn sang lượt Dispatcher kế tiếp (KHÔNG gọi ScrollToVerticalOffset ngay trong lúc grid
                // nguồn đang tự layout/arrange do chính cú cuộn này) — gọi đồng bộ ngay tại đây từng gây
                // "Index was outside the bounds of the array" ngẫu nhiên do đụng vào bảng ghi container
                // đang tái sử dụng (virtualization) của DataGrid đối tác giữa chừng 1 pass layout khác.
                partner.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (FindScrollViewer(partner) is not ScrollViewer partnerSv) return;
                    partner.SetValue(SyncingScrollProperty, true);
                    try { partnerSv.ScrollToVerticalOffset(offset); }
                    finally { partner.SetValue(SyncingScrollProperty, false); }
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
        }
        if (grid.IsLoaded) Hook(); else grid.Loaded += (_, _) => Hook();
    }

    private static ScrollViewer FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer found) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (result != null) return result;
        }
        return null;
    }

    private static void WireSelection(DataGrid grid)
    {
        grid.SelectionChanged += (_, _) =>
        {
            if ((bool)grid.GetValue(SyncingProperty)) return;
            if (grid.GetValue(PartnerProperty) is not DataGrid partner) return;
            partner.SetValue(SyncingProperty, true);
            try { partner.SelectedItem = grid.SelectedItem; }
            finally { partner.SetValue(SyncingProperty, false); }
        };
    }

    private static void OnAutoHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || e.NewValue is not true) return;
        grid.LoadingRow += (_, ev) =>
        {
            var row = ev.Row;
            if ((bool)row.GetValue(WiredProperty)) return;
            row.SetValue(WiredProperty, true);
            row.MouseEnter += (_, _) => SetHover(grid, row.GetIndex(), true);
            row.MouseLeave += (_, _) => SetHover(grid, row.GetIndex(), false);
        };
    }

    private static void SetHover(DataGrid grid, int index, bool value)
    {
        if (grid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow r1) RowHover.SetIsHovered(r1, value);
        if (grid.GetValue(PartnerProperty) is DataGrid partner &&
            partner.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow r2)
            RowHover.SetIsHovered(r2, value);
    }
}

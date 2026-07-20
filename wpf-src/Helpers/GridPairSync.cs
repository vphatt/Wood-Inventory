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

    // Offset mà CODE vừa chủ động cuộn grid này tới (NaN = chưa/không phải cuộn do đồng bộ). Dùng để nhận diện
    // cú ScrollChanged "echo" (do sync sinh ra) và bỏ qua nó — xem WireScroll.
    private static readonly DependencyProperty LastSyncedProperty =
        DependencyProperty.RegisterAttached("LastSynced", typeof(double), typeof(GridPairSync), new PropertyMetadata(double.NaN));
    private static double GetLastSynced(DependencyObject d) => (double)d.GetValue(LastSyncedProperty);
    private static void SetLastSynced(DependencyObject d, double v) => d.SetValue(LastSyncedProperty, v);

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
                // Cú cuộn này là ECHO do CHÍNH code vừa đồng bộ grid NÀY (offset = giá trị vừa set) → tiêu thụ,
                // TUYỆT ĐỐI không sync ngược. Đây là điểm triệt vòng lặp: khi cuộn nhanh, grid bị-đồng-bộ (lag) KHÔNG
                // được kéo grid-đang-dẫn về vị trí cũ nữa. Đặt NaN sau khi tiêu thụ để lần cuộn TAY tới đúng offset
                // đó về sau không bị nhầm là echo.
                if (Math.Abs(sv.VerticalOffset - GetLastSynced(grid)) < 0.5) { SetLastSynced(grid, double.NaN); return; }
                if (grid.GetValue(PartnerProperty) is not DataGrid partner) return;
                // Hoãn 1 lượt Dispatcher (tránh crash "Index outside bounds" khi cuộn giữa layout) + đọc offset HIỆN TẠI
                // trong lambda để GỘP các cú cuộn nhanh liên tiếp về vị trí cuối (không dùng offset cũ lúc schedule).
                partner.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Nuốt lỗi tạm thời của DataGrid ảo hoá (vd ScrollChanged bắn ngay lúc CollectionView.Refresh do
                    // tìm kiếm/lọc đang rebuild container → ScrollToVerticalOffset ném "Index was outside the bounds
                    // of the array"). Đồng bộ cuộn chỉ là cosmetic — bỏ qua 1 khung, không cho nổi dialog lỗi.
                    try
                    {
                        if (FindScrollViewer(partner) is not ScrollViewer partnerSv) return;
                        var target = sv.VerticalOffset;
                        if (Math.Abs(partnerSv.VerticalOffset - target) < 0.5) return;
                        SetLastSynced(partner, target);   // đánh dấu để cú ScrollChanged dội lại của partner nhận ra là echo
                        partnerSv.ScrollToVerticalOffset(target);
                    }
                    catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException) { }
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

    // index đến từ row.GetIndex() lúc MouseEnter/Leave — khi ảo hóa recycle container, index có thể đã stale/ngoài
    // range (âm hoặc ≥ số dòng), lúc đó ContainerFromIndex NÉM IndexOutOfRange (không trả null) → phải chặn range
    // TRƯỚC + try/catch phòng hờ, nếu không sẽ crash "Index was outside the bounds of the array" khi rê chuột lúc cuộn.
    private static void SetHover(DataGrid grid, int index, bool value)
    {
        try
        {
            if (index >= 0 && index < grid.Items.Count
                && grid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow r1)
                RowHover.SetIsHovered(r1, value);
            if (grid.GetValue(PartnerProperty) is DataGrid partner && index >= 0 && index < partner.Items.Count
                && partner.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow r2)
                RowHover.SetIsHovered(r2, value);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException) { }
    }
}

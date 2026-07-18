using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WoodInventory.Helpers;

/// <summary>Tiện ích cuộn UI dùng chung.</summary>
public static class UiScroll
{
    /// <summary>Cuộn ScrollViewer GẦN NHẤT (tổ tiên trong visual tree) chứa <paramref name="child"/> về đầu trang.
    /// Dùng khi mở form Xem/Sửa ở đầu trang để LUÔN kéo viewport lên thấy form — kể cả khi đang mở view mode của
    /// dòng khác (lúc đó Visibility không đổi nên WPF không tự cuộn). Truyền 1 element nằm TRONG form (vd AddFormPanel)
    /// — nó không nằm trong DataGrid nên đi ngược lên chắc chắn ra ScrollViewer gốc của trang.</summary>
    public static void ToTop(DependencyObject child)
    {
        var d = child;
        while (d != null && d is not ScrollViewer) d = VisualTreeHelper.GetParent(d);
        (d as ScrollViewer)?.ScrollToTop();
    }
}

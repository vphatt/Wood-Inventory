using System.Windows;

namespace WoodInventory.Helpers;

/// <summary>
/// Cờ hover gắn ngoài (attached property) cho DataGridRow — thay cho IsMouseOver gốc của WPF, để
/// <see cref="GridPairSync"/> có thể bật/tắt hiệu ứng hover trên CẢ HAI DataGrid (nội dung + thao tác)
/// dù chuột vật lý chỉ đang ở trên 1 trong 2 control.
/// </summary>
public static class RowHover
{
    public static readonly DependencyProperty IsHoveredProperty =
        DependencyProperty.RegisterAttached("IsHovered", typeof(bool), typeof(RowHover),
            new PropertyMetadata(false));

    public static bool GetIsHovered(DependencyObject obj) => (bool)obj.GetValue(IsHoveredProperty);
    public static void SetIsHovered(DependencyObject obj, bool value) => obj.SetValue(IsHoveredProperty, value);
}

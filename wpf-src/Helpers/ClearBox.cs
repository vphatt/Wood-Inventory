using System.Windows;
using System.Windows.Controls;

namespace WoodInventory.Helpers;

/// <summary>
/// Attached behavior cho nút "x" xóa nội dung một ô search: gắn <c>ClearBox.Target</c> = TextBox cần xóa.
/// Bấm nút → xóa text ô đó; đồng thời tự ẩn nút khi ô rỗng, hiện khi có nội dung. Dùng chung mọi search box
/// (đều đặt tên <c>SearchBox</c>) nên không cần handler riêng ở từng View.
/// </summary>
public static class ClearBox
{
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.RegisterAttached("Target", typeof(TextBox), typeof(ClearBox),
            new PropertyMetadata(null, OnTargetChanged));

    public static void SetTarget(DependencyObject o, TextBox v) => o.SetValue(TargetProperty, v);
    public static TextBox GetTarget(DependencyObject o) => (TextBox)o.GetValue(TargetProperty);

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button btn || e.NewValue is not TextBox box) return;
        btn.Click += (_, _) => box.Clear();
        void Update() => btn.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Collapsed : Visibility.Visible;
        box.TextChanged += (_, _) => Update();
        Update();   // trạng thái ban đầu (ô rỗng → ẩn)
    }
}

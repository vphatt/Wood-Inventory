using System.Windows;
using System.Windows.Controls;

namespace WoodInventory.Helpers;

/// <summary>
/// Đánh số STT cho DataGrid theo VỊ TRÍ THẬT của dòng (DataGridRow.GetIndex) qua sự kiện LoadingRow —
/// ĐÚNG kể cả khi ảo hóa (khác AlternationIndex bị lệch/nhảy ~99999 khi cuộn với virtualization, cả Recycling
/// lẫn Standard). Bật bằng <c>helpers:RowNumbering.Enabled="True"</c> trên DataGrid (đặt trong style DataTable).
/// Cột STT bind vào <c>(helpers:RowNumbering.Number)</c> của DataGridRow (1-based, tự cập nhật khi dòng được
/// realize lại lúc cuộn/lọc).
/// </summary>
public static class RowNumbering
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(RowNumbering),
            new PropertyMetadata(false, OnEnabledChanged));
    public static void SetEnabled(DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    public static readonly DependencyProperty NumberProperty =
        DependencyProperty.RegisterAttached("Number", typeof(int), typeof(RowNumbering), new PropertyMetadata(0));
    public static void SetNumber(DependencyObject o, int v) => o.SetValue(NumberProperty, v);
    public static int GetNumber(DependencyObject o) => (int)o.GetValue(NumberProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid g) return;
        g.LoadingRow -= OnLoadingRow;
        if (e.NewValue is true) g.LoadingRow += OnLoadingRow;
    }

    // Fires mỗi khi 1 dòng được prepare (kể cả container tái dùng khi Recycling) → GetIndex() luôn là vị trí hiện tại.
    private static void OnLoadingRow(object sender, DataGridRowEventArgs e) =>
        e.Row.SetValue(NumberProperty, e.Row.GetIndex() + 1);
}

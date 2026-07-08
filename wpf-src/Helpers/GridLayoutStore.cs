using System.Windows.Controls;

namespace TimberFlowDesktop.Helpers;

/// <summary>
/// (ĐÃ VÔ HIỆU HÓA) Trước đây lưu/khôi phục bố cục cột DataGrid theo user (thứ tự + độ rộng).
/// Từ khi các bảng chuyển sang **độ rộng cố định + tắt resize/reorder** (xem style DataTable trong
/// App.xaml), không còn gì để lưu nữa. Giữ <see cref="Attach"/> làm no-op để không phải sửa mọi
/// call site và để layout cũ (grid-layout.json) không đè lên độ rộng tuned mới.
/// </summary>
public static class GridLayoutStore
{
    public static void Attach(DataGrid grid, string key) { /* no-op: cột cố định, không resize/reorder */ }
}

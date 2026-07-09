using System.Globalization;
using System.Windows.Data;

namespace WoodInventory.Helpers;

/// <summary>
/// Chuyển AlternationIndex (0-based, do WPF tự gán theo vị trí hiển thị hiện tại của dòng
/// trong DataGrid) sang STT hiển thị (1-based) — luôn tăng dần theo đúng thứ tự đang hiển thị
/// dù đã sort/lọc/tìm kiếm.
/// </summary>
public sealed class RowNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i ? (i + 1).ToString() : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

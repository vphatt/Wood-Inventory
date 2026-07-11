using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WoodInventory.Data;
using WoodInventory.Helpers;

namespace WoodInventory;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, $"{Lang.T("Common.AppTitle")} — {Lang.T("Common.ErrorTitle")}",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Khởi tạo SQLite + seed dữ liệu mẫu trước khi mở cửa sổ chính
        AppState.Initialize();
        LanguageService.Instance.Initialize(AppState.Settings.Language);

        new MainWindow().Show();
    }

    // ---------------- Bộ chọn Tháng/Năm tự làm cho popup Calendar của DatePicker ----------------
    // Calendar.DisplayMode đổi đúng khi bấm PART_HeaderButton nhưng KHÔNG tự chuyển hiển thị
    // PART_MonthView/PART_YearView theo (bẫy đã xác nhận bằng log, xem CLAUDE.md) — nên tự quản lý
    // toggle Visibility hoàn toàn ở đây, không phụ thuộc cơ chế nội bộ của Calendar.

    /// <summary>Bấm tiêu đề header: hiện/ẩn bộ chọn Tháng (MonthPickerView), ẩn/hiện lại lưới ngày.</summary>
    private void CalendarHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn || btn.TemplatedParent is not Control item || item.Template == null) return;
        var cal = FindAncestor<Calendar>(btn);
        if (cal == null) return;

        var monthView = item.Template.FindName("PART_MonthView", item) as UIElement;
        var picker = item.Template.FindName("MonthPickerView", item) as UIElement;
        var yearText = item.Template.FindName("MonthPickerYearText", item) as TextBlock;
        if (monthView == null || picker == null || yearText == null) return;

        if (picker.Visibility == Visibility.Visible)
        {
            picker.Visibility = Visibility.Collapsed;
            monthView.Visibility = Visibility.Visible;
        }
        else
        {
            yearText.Text = cal.DisplayDate.Year.ToString();
            monthView.Visibility = Visibility.Collapsed;
            picker.Visibility = Visibility.Visible;
        }
    }

    private void MonthPickerPrevYear_Click(object sender, RoutedEventArgs e) => ShiftPickerYear(sender, -1);
    private void MonthPickerNextYear_Click(object sender, RoutedEventArgs e) => ShiftPickerYear(sender, 1);

    private static void ShiftPickerYear(object sender, int delta)
    {
        if (sender is not FrameworkElement btn || btn.TemplatedParent is not Control item || item.Template == null) return;
        if (item.Template.FindName("MonthPickerYearText", item) is not TextBlock yearText) return;
        if (int.TryParse(yearText.Text, out var y)) yearText.Text = (y + delta).ToString();
    }

    /// <summary>Bấm 1 tháng trong lưới: nhảy Calendar.DisplayDate tới tháng/năm đó rồi đóng bộ chọn lại.</summary>
    private void MonthPickerMonth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button monthBtn || monthBtn.TemplatedParent is not Control item || item.Template == null) return;
        var cal = FindAncestor<Calendar>(monthBtn);
        if (cal == null) return;

        var yearText = item.Template.FindName("MonthPickerYearText", item) as TextBlock;
        var monthView = item.Template.FindName("PART_MonthView", item) as UIElement;
        var picker = item.Template.FindName("MonthPickerView", item) as UIElement;
        if (yearText == null || monthView == null || picker == null) return;

        var year = int.TryParse(yearText.Text, out var y) ? y : cal.DisplayDate.Year;
        var month = monthBtn.Tag is string tag && int.TryParse(tag, out var m) ? m : 1;

        cal.DisplayDate = new DateTime(year, month, 1);
        picker.Visibility = Visibility.Collapsed;
        monthView.Visibility = Visibility.Visible;
    }

    private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }
}

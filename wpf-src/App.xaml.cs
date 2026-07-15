using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            AppDialog.Show(args.Exception.Message, $"{Lang.T("Common.AppTitle")} — {Lang.T("Common.ErrorTitle")}",
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

    /// <summary>3 lưới của popup Calendar — chỉ 1 cái hiện tại 1 thời điểm.</summary>
    private enum CalView { Day, Month, Year }

    /// <summary>
    /// Bấm tiêu đề header: xoay vòng Ngày → Tháng → Năm → Ngày.
    /// </summary>
    private void CalendarHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn || btn.TemplatedParent is not Control item || item.Template == null) return;
        var cal = FindAncestor<Calendar>(btn);
        if (cal == null) return;

        var monthPicker = item.Template.FindName("MonthPickerView", item) as UIElement;
        var yearPicker = item.Template.FindName("YearPickerView", item) as UIElement;
        var monthYearText = item.Template.FindName("MonthPickerYearText", item) as TextBlock;
        if (monthPicker == null || yearPicker == null || monthYearText == null) return;

        if (monthPicker.Visibility == Visibility.Visible)
        {
            // Đang chọn Tháng → sang chọn Năm. Canh trang theo THẬP KỶ (12 ô = thập kỷ + 1 năm đệm mỗi đầu,
            // vd 2019..2030 cho thập kỷ 2020-2029) để khớp nhãn thập kỷ mà header của Calendar tự hiện.
            var year = int.TryParse(monthYearText.Text, out var y) ? y : cal.DisplayDate.Year;
            FillYearPicker(item, year - (year % 10) - 1);
            ShowView(item, CalView.Year);
        }
        else if (yearPicker.Visibility == Visibility.Visible)
        {
            ShowView(item, CalView.Day);        // Đang chọn Năm → quay về lưới Ngày
        }
        else
        {
            monthYearText.Text = cal.DisplayDate.Year.ToString();
            ShowView(item, CalView.Month);      // Đang ở lưới Ngày → sang chọn Tháng
        }
    }

    /// <summary>
    /// Bật đúng 1 trong 3 lưới. Hàng tên thứ (T2–CN) là Grid TĨNH riêng, không nằm trong PART_MonthView,
    /// nên phải tự ẩn khi rời lưới Ngày — nếu không nó vẫn nổi đè lên lưới Tháng/Năm.
    /// </summary>
    private static void ShowView(Control item, CalView view)
    {
        void Set(string name, bool visible)
        {
            if (item.Template.FindName(name, item) is UIElement el)
                el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        Set("DayNamesRow", view == CalView.Day);
        Set("PART_MonthView", view == CalView.Day);
        Set("MonthPickerView", view == CalView.Month);
        Set("YearPickerView", view == CalView.Year);
    }

    /// <summary>Đổ 12 năm (từ startYear) vào lưới chọn năm + cập nhật nhãn khoảng năm (Tag giữ startYear để lùi/tiến trang).</summary>
    private static void FillYearPicker(Control item, int startYear)
    {
        if (item.Template.FindName("YearPickerGrid", item) is not UniformGrid grid) return;
        for (var i = 0; i < grid.Children.Count && i < 12; i++)
        {
            if (grid.Children[i] is not Button b) continue;
            var y = startYear + i;
            b.Content = y.ToString();
            b.Tag = y.ToString();
        }
        if (item.Template.FindName("YearPickerRangeText", item) is TextBlock t)
        {
            // Nhãn = đúng thập kỷ (bỏ 2 năm đệm ở hai đầu) cho khớp header của Calendar.
            t.Text = $"{startYear + 1} – {startYear + 10}";
            t.Tag = startYear;
        }
    }

    private void YearPickerPrev_Click(object sender, RoutedEventArgs e) => ShiftYearPage(sender, -10);
    private void YearPickerNext_Click(object sender, RoutedEventArgs e) => ShiftYearPage(sender, 10);

    private static void ShiftYearPage(object sender, int delta)
    {
        if (sender is not FrameworkElement btn || btn.TemplatedParent is not Control item || item.Template == null) return;
        if (item.Template.FindName("YearPickerRangeText", item) is not TextBlock t || t.Tag is not int start) return;
        FillYearPicker(item, start + delta);
    }

    /// <summary>Bấm 1 năm: quay lại lưới chọn Tháng của đúng năm đó (drill-down Năm → Tháng → Ngày).</summary>
    private void YearPickerYear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button yearBtn || yearBtn.TemplatedParent is not Control item || item.Template == null) return;
        if (yearBtn.Tag is not string tag || !int.TryParse(tag, out var year)) return;

        if (item.Template.FindName("MonthPickerYearText", item) is TextBlock yt) yt.Text = year.ToString();
        ShowView(item, CalView.Month);
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

        if (item.Template.FindName("MonthPickerYearText", item) is not TextBlock yearText) return;

        var year = int.TryParse(yearText.Text, out var y) ? y : cal.DisplayDate.Year;
        var month = monthBtn.Tag is string tag && int.TryParse(tag, out var m) ? m : 1;

        cal.DisplayDate = new DateTime(year, month, 1);
        ShowView(item, CalView.Day);
    }

    private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }
}

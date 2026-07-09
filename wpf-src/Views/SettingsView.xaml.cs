using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using WoodInventory.Data;
using WoodInventory.Domain;
using WoodInventory.Helpers;

namespace WoodInventory.Views;

public partial class SettingsView : UserControl, IModuleView
{
    public SettingsView()
    {
        InitializeComponent();
        InitTaxCombo();
        RefreshView();
    }

    private void InitTaxCombo()
    {
        FTaxPercent.Items.Clear();
        foreach (var t in new[] { "0", "5", "8", "10" })
            FTaxPercent.Items.Add(new ComboBoxItem { Content = $"{t}%", Tag = t });
    }

    public void RefreshView()
    {
        ClearWarnings();
        var s = AppState.Settings;
        FCompanyName.Text = s.CompanyName;
        FCompanyTaxCode.Text = s.CompanyTaxCode;
        FCompanyAddress.Text = s.CompanyAddress;
        FCompanyPhone.Text = s.CompanyPhone;
        FExchangeRate.Text = Fmt.Num((double)s.DefaultExchangeRate);
        FVolumeDecimals.Text = s.DefaultVolumeDecimals.ToString();
        FLowStockThreshold.Text = s.LowStockThreshold.ToString();

        var taxTag = Fmt.Num((double)s.DefaultTaxPercent);
        FTaxPercent.SelectedIndex = -1;
        foreach (ComboBoxItem it in FTaxPercent.Items)
            if ((it.Tag as string) == taxTag) { FTaxPercent.SelectedItem = it; break; }
        if (FTaxPercent.SelectedIndex < 0) FTaxPercent.SelectedIndex = 3; // mặc định 10% nếu giá trị lạ
    }

    // ---------------- Cảnh báo inline ----------------

    private static void ShowWarn(TextBlock w, string msg)
    {
        w.Text = msg;
        w.Visibility = Visibility.Visible;
    }

    private void ClearWarnings() =>
        WCompanyName.Visibility = WExchangeRate.Visibility = WVolumeDecimals.Visibility = WLowStockThreshold.Visibility
            = Visibility.Collapsed;

    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TextBlock w) w.Visibility = Visibility.Collapsed;
    }

    private void BtnDiscard_Click(object sender, RoutedEventArgs e) => RefreshView();

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ClearWarnings();
        var ok = true;

        if (string.IsNullOrWhiteSpace(FCompanyName.Text)) { ShowWarn(WCompanyName, "Vui lòng nhập tên công ty."); ok = false; }

        var exchangeRate = Fmt.ParseNum(FExchangeRate.Text);
        if (exchangeRate <= 0) { ShowWarn(WExchangeRate, "Tỷ giá phải lớn hơn 0."); ok = false; }

        var decimals = (int)Fmt.ParseNum(FVolumeDecimals.Text);
        if (decimals < 0 || decimals > 15) { ShowWarn(WVolumeDecimals, "Số chữ số thập phân phải từ 0 đến 15."); ok = false; }

        var lowStock = (int)Fmt.ParseNum(FLowStockThreshold.Text);
        if (lowStock < 0) { ShowWarn(WLowStockThreshold, "Ngưỡng cảnh báo không được âm."); ok = false; }

        if (!ok) return;

        AppState.UpdateSettings(new AppSettings
        {
            CompanyName = FCompanyName.Text,
            CompanyTaxCode = FCompanyTaxCode.Text,
            CompanyAddress = FCompanyAddress.Text,
            CompanyPhone = FCompanyPhone.Text,
            DefaultExchangeRate = (decimal)exchangeRate,
            DefaultTaxPercent = (decimal)Fmt.ParseNum((FTaxPercent.SelectedItem as ComboBoxItem)?.Tag as string ?? "10"),
            DefaultVolumeDecimals = decimals,
            LowStockThreshold = lowStock
        });

        MessageBox.Show("Đã lưu cài đặt.", "Quản Lý Gỗ", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------------- Dữ liệu: sao lưu / phục hồi / mở thư mục ----------------

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Sao lưu dữ liệu",
            Filter = "SQLite Database (*.db)|*.db",
            FileName = $"woodinventory-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            // Gộp WAL vào file .db chính trước khi copy để bản sao lưu chứa đủ dữ liệu đã commit.
            using (var db = new AppDbContext())
            {
                db.Database.OpenConnection();
                using var cmd = db.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            File.Copy(AppDbContext.DbPath, dialog.FileName, overwrite: true);
            MessageBox.Show($"Đã sao lưu vào:\n{dialog.FileName}", "Quản Lý Gỗ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Sao lưu thất bại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Chọn file sao lưu", Filter = "SQLite Database (*.db)|*.db" };
        if (dialog.ShowDialog() != true) return;

        var confirm = MessageBox.Show(
            "Toàn bộ dữ liệu hiện tại sẽ bị THAY THẾ bằng file sao lưu đã chọn và không thể hoàn tác.\n" +
            "Ứng dụng sẽ khởi động lại ngay sau khi phục hồi xong. Tiếp tục?",
            "Xác nhận phục hồi dữ liệu", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            SqliteConnection.ClearAllPools();   // nhả handle file đang giữ bởi connection pool trước khi ghi đè
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = AppDbContext.DbPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
            File.Copy(dialog.FileName, AppDbContext.DbPath);

            Process.Start(Environment.ProcessPath!);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Phục hồi thất bại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(AppDbContext.DbPath)!, UseShellExecute = true });
}

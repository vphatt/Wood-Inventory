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
    private bool _loadingLanguage;

    public SettingsView()
    {
        InitializeComponent();
        InitTaxCombo();
        InitLanguageCombo();
        RefreshView();
    }

    private void InitTaxCombo()
    {
        FTaxPercent.Items.Clear();
        foreach (var t in new[] { "0", "5", "8", "10" })
            FTaxPercent.Items.Add(new ComboBoxItem { Content = $"{t}%", Tag = t });
    }

    private void InitLanguageCombo()
    {
        FLanguage.Items.Clear();
        foreach (var (code, label) in LanguageService.Available)
            FLanguage.Items.Add(new ComboBoxItem { Content = label, Tag = code });
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

        _loadingLanguage = true;
        FLanguage.SelectedIndex = -1;
        foreach (ComboBoxItem it in FLanguage.Items)
            if ((it.Tag as string) == s.Language) { FLanguage.SelectedItem = it; break; }
        if (FLanguage.SelectedIndex < 0) FLanguage.SelectedIndex = 0;
        _loadingLanguage = false;
    }

    /// <summary>Đổi ngôn ngữ áp dụng NGAY (hot-swap) — không qua nút "Lưu cài đặt" chung như các field khác.</summary>
    private void FLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingLanguage) return;
        var code = (FLanguage.SelectedItem as ComboBoxItem)?.Tag as string ?? "vi";
        Lang.SetLanguage(code);
        AppState.SetLanguage(code);
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

        if (string.IsNullOrWhiteSpace(FCompanyName.Text)) { ShowWarn(WCompanyName, Lang.T("Settings.Warn.CompanyName")); ok = false; }

        var exchangeRate = Fmt.ParseNum(FExchangeRate.Text);
        if (exchangeRate <= 0) { ShowWarn(WExchangeRate, Lang.T("Settings.Warn.ExchangeRate")); ok = false; }

        var decimals = (int)Fmt.ParseNum(FVolumeDecimals.Text);
        if (decimals < 0 || decimals > 15) { ShowWarn(WVolumeDecimals, Lang.T("Settings.Warn.VolumeDecimals")); ok = false; }

        var lowStock = (int)Fmt.ParseNum(FLowStockThreshold.Text);
        if (lowStock < 0) { ShowWarn(WLowStockThreshold, Lang.T("Settings.Warn.LowStock")); ok = false; }

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

        MessageBox.Show(Lang.T("Settings.SavedMessage"), Lang.T("Common.AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------------- Dữ liệu: sao lưu / phục hồi / mở thư mục ----------------

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Lang.T("Settings.Backup.DialogTitle"),
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
            MessageBox.Show(Lang.T("Settings.Backup.SuccessMessage", dialog.FileName), Lang.T("Common.AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.T("Settings.Backup.FailMessage", ex.Message), Lang.T("Common.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Lang.T("Settings.Restore.DialogTitle"), Filter = "SQLite Database (*.db)|*.db" };
        if (dialog.ShowDialog() != true) return;

        var confirm = MessageBox.Show(
            Lang.T("Settings.Restore.ConfirmMessage"),
            Lang.T("Settings.Restore.ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
            MessageBox.Show(Lang.T("Settings.Restore.FailMessage", ex.Message), Lang.T("Common.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(AppDbContext.DbPath)!, UseShellExecute = true });
}

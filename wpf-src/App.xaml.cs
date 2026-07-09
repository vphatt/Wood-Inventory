using System.Windows;
using WoodInventory.Data;

namespace WoodInventory;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Quản Lý Gỗ — Lỗi",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Khởi tạo SQLite + seed dữ liệu mẫu trước khi mở cửa sổ chính
        AppState.Initialize();

        new MainWindow().Show();
    }
}

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
            // Nuốt IM LẶNG lỗi ảo-hoá DataGrid tạm thời của WPF ("Index was outside the bounds of the array")
            // khi cuộn/đồng bộ đúng lúc CollectionView.Refresh (tìm kiếm/lọc) đang rebuild container — benign,
            // lệch pha qua layout pass nên try/catch cục bộ ở GridPairSync không phải lúc nào cũng tóm được.
            // CHỈ bỏ qua khi stack đúng là của tầng ảo-hoá/scroll WPF, để KHÔNG che lỗi IndexOutOfRange thật của app.
            if (IsBenignVirtualizationGlitch(args.Exception))
            {
                args.Handled = true;
                return;
            }
            AppDialog.Show(args.Exception.Message, $"{Lang.T("Common.AppTitle")} — {Lang.T("Common.ErrorTitle")}",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Khởi tạo SQLite + seed dữ liệu mẫu trước khi mở cửa sổ chính
        AppState.Initialize();
        LanguageService.Instance.Initialize(AppState.Settings.Language);

        new MainWindow().Show();
    }

    /// <summary>Đúng là lỗi ảo-hoá/scroll tạm thời của DataGrid WPF (benign) chứ không phải lỗi thật của app?</summary>
    private static bool IsBenignVirtualizationGlitch(Exception ex)
    {
        if (ex is not (IndexOutOfRangeException or ArgumentOutOfRangeException)) return false;
        var st = ex.StackTrace ?? "";
        // Stack phải nằm trong tầng dựng UI của WPF (panel ảo hoá / scroll / DataGrid), KHÔNG có frame code app.
        return (st.Contains("VirtualizingStackPanel") || st.Contains("ScrollViewer") || st.Contains("ScrollContentPresenter")
                || st.Contains("DataGridRowsPresenter") || st.Contains("VirtualizingPanel") || st.Contains("ScrollData"))
               && !st.Contains("WoodInventory.");
    }
}

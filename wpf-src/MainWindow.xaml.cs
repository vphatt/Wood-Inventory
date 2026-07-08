using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TimberFlowDesktop.Data;
using TimberFlowDesktop.Views;

namespace TimberFlowDesktop;

/// <summary>
/// Cửa sổ chính: sidebar điều hướng + dải tab động (mở/đóng tab như bản web)
/// + breadcrumb + vùng nội dung + thanh trạng thái.
/// </summary>
public partial class MainWindow : Window
{
    private sealed record NavItem(string Module, string Label, string Glyph);

    private sealed class WorkTab
    {
        public string Module;
        public string Title;
        public Border Chip;          // phần tử hiển thị trên dải tab
    }

    private static readonly NavItem[] NavItems =
    {
        new("dashboard",  "Bảng Điều Khiển",        ""),
        new("categories", "Phân Loại Gỗ",         ""),
        new("suppliers",  "Nhà Cung Cấp",           ""),
        new("lots",       "Quản Lý Kiện Gỗ (Lots)", ""),
        new("receipts",   "Nhập Kho Gỗ",            ""),
        new("issues",     "Xuất Kho Gỗ",            ""),
        new("dotnet",     "Mã C# .NET / WPF",       ""),
    };

    private readonly List<WorkTab> _tabs = new();
    private readonly Dictionary<string, UserControl> _viewCache = new();
    private string _activeModule = "dashboard";

    public MainWindow()
    {
        InitializeComponent();

        AppState.Changed += OnDataChanged;
        Unloaded += (_, _) => AppState.Changed -= OnDataChanged;

        OpenModule("dashboard");

        // Hỗ trợ mở thẳng một module khi khởi động: TimberFlowDesktop.exe --module lots
        var args = Environment.GetCommandLineArgs();
        var idx = Array.IndexOf(args, "--module");
        if (idx >= 0 && idx + 1 < args.Length)
            OpenModule(args[idx + 1]);

        StartPulseAnimation();
    }

    private void OnDataChanged()
    {
        BuildNav();
        if (_viewCache.TryGetValue(_activeModule, out var view) && view is IModuleView refreshable)
            refreshable.RefreshView();
    }

    private static string GetModuleTitle(string module) => module switch
    {
        "dashboard" => "Bảng Điều Khiển",
        "categories" => "Phân Loại Gỗ",
        "suppliers" => "Nhà Cung Cấp",
        "lots" => "Kiện Gỗ (Lots)",
        "quotations" => "Báo Giá Gỗ",
        "receipts" => "Nhập Kho Gỗ",
        "issues" => "Xuất Kho Gỗ",
        "dotnet" => "Mã C# .NET WPF",
        _ => "Module"
    };

    /// <summary>Mở module: nếu tab đã tồn tại thì kích hoạt, chưa có thì thêm tab mới (giống web).</summary>
    public void OpenModule(string module)
    {
        var existing = _tabs.FirstOrDefault(t => t.Module == module);
        if (existing == null)
        {
            existing = new WorkTab { Module = module, Title = GetModuleTitle(module) };
            _tabs.Add(existing);
        }
        ActivateTab(existing);
    }

    private Action _breadcrumbBack;

    /// <summary>Hiện/ẩn cấp breadcrumb chi tiết (vd Báo Giá Gỗ / Tên NCC). detail=null để ẩn.</summary>
    public void SetBreadcrumbDetail(string detail, Action onBack = null)
    {
        _breadcrumbBack = onBack;
        if (string.IsNullOrEmpty(detail))
        {
            BreadcrumbSep2.Visibility = Visibility.Collapsed;
            BreadcrumbDetail.Visibility = Visibility.Collapsed;
            BreadcrumbCurrent.Foreground = (Brush)FindResource("Slate900");
            BreadcrumbCurrent.Cursor = null;
        }
        else
        {
            BreadcrumbDetail.Text = detail;
            BreadcrumbSep2.Visibility = Visibility.Visible;
            BreadcrumbDetail.Visibility = Visibility.Visible;
            BreadcrumbCurrent.Foreground = (Brush)FindResource("Blue600");  // segment cha thành link quay lại
            BreadcrumbCurrent.Cursor = Cursors.Hand;
        }
    }

    private void BreadcrumbCurrent_Click(object sender, MouseButtonEventArgs e) => _breadcrumbBack?.Invoke();

    private void ActivateTab(WorkTab tab)
    {
        _activeModule = tab.Module;
        BreadcrumbCurrent.Text = tab.Title;
        SetBreadcrumbDetail(null);   // mặc định ẩn cấp chi tiết; view sẽ tự bật lại nếu đang ở detail

        if (!_viewCache.TryGetValue(tab.Module, out var view))
        {
            view = CreateView(tab.Module);
            _viewCache[tab.Module] = view;
        }
        (view as IModuleView)?.RefreshView();
        ContentHost.Content = view;

        BuildTabStrip();
        BuildNav();
    }

    private void CloseTab(WorkTab tab)
    {
        if (tab.Module == "dashboard") return; // không đóng tab dashboard

        _tabs.Remove(tab);
        _viewCache.Remove(tab.Module);

        if (_activeModule == tab.Module)
            ActivateTab(_tabs[^1]);
        else
            BuildTabStrip();
    }

    private static UserControl CreateView(string module) => module switch
    {
        "dashboard" => new DashboardView(),
        "categories" => new WoodCategoriesView(),
        "suppliers" => new SuppliersView(),
        "lots" => new LotsView(),
        "quotations" => new QuotationsView(),
        "receipts" => new ReceiptsView(),
        "issues" => new IssuesView(),
        "dotnet" => new DotNetView(),
        _ => new DashboardView()
    };

    // ---------------- Sidebar ----------------

    private void BuildNav()
    {
        // Giữ lại tiêu đề nhóm (phần tử đầu), xóa các nút cũ
        while (NavPanel.Children.Count > 1)
            NavPanel.Children.RemoveAt(1);

        foreach (var item in NavItems)
        {
            var isActive = _activeModule == item.Module;

            var icon = new TextBlock
            {
                Text = item.Glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = isActive
                    ? (Brush)FindResource("Blue400")
                    : (Brush)FindResource("Slate500")
            };

            var label = new TextBlock
            {
                Text = item.Label,
                FontSize = 12,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = isActive ? Brushes.White : (Brush)FindResource("Slate400"),
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Medium
            };

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(icon);
            left.Children.Add(label);

            var row = new Grid();
            row.Children.Add(left);

            // Huy hiệu (badge): số kiện gỗ cho mục Lots, nhãn "Clean" cho mục .NET
            if (item.Module == "lots" || item.Module == "dotnet")
            {
                var lowStock = AppState.LowStockCount > 0 && item.Module == "lots";
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(6, 2, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = isActive
                        ? new SolidColorBrush(Color.FromArgb(0x33, 0x3B, 0x82, 0xF6))
                        : lowStock
                            ? new SolidColorBrush(Color.FromArgb(0x33, 0xF4, 0x3F, 0x5E))
                            : (Brush)FindResource("SideHover"),
                    Child = new TextBlock
                    {
                        Text = item.Module == "lots" ? AppState.Lots.Count.ToString() : "Clean",
                        FontFamily = (FontFamily)FindResource("FontMono"),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = isActive
                            ? (Brush)FindResource("Blue300")
                            : lowStock
                                ? (Brush)FindResource("Rose300")
                                : (Brush)FindResource("Slate500")
                    }
                };
                row.Children.Add(badge);
            }

            var border = new Border
            {
                Background = isActive ? (Brush)FindResource("SideHover") : Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Child = row,
                Tag = item.Module
            };

            border.MouseLeftButtonDown += (_, _) => OpenModule(item.Module);
            if (!isActive)
            {
                border.MouseEnter += (_, _) =>
                {
                    border.Background = (Brush)FindResource("SideHover");
                    label.Foreground = Brushes.White;
                    icon.Foreground = (Brush)FindResource("Slate300");
                };
                border.MouseLeave += (_, _) =>
                {
                    border.Background = Brushes.Transparent;
                    label.Foreground = (Brush)FindResource("Slate400");
                    icon.Foreground = (Brush)FindResource("Slate500");
                };
            }

            NavPanel.Children.Add(border);
        }
    }

    // ---------------- Dải tab ----------------

    private void BuildTabStrip()
    {
        TabStrip.Children.Clear();

        foreach (var tab in _tabs)
        {
            var isActive = tab.Module == _activeModule;

            var titleBlock = new TextBlock
            {
                Text = tab.Title,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Medium,
                Foreground = isActive ? (Brush)FindResource("Blue600") : (Brush)FindResource("Slate500")
            };

            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(titleBlock);

            if (tab.Module != "dashboard")
            {
                var closeBtn = new Border
                {
                    Width = 16, Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = "",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 8,
                        Foreground = (Brush)FindResource("Slate400"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                closeBtn.MouseEnter += (_, _) => closeBtn.Background = (Brush)FindResource("Slate200");
                closeBtn.MouseLeave += (_, _) => closeBtn.Background = Brushes.Transparent;
                closeBtn.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    CloseTab(tab);
                };
                inner.Children.Add(closeBtn);
            }

            var chip = new Border
            {
                Height = 39,
                Padding = new Thickness(16, 0, 16, 0),
                Background = isActive ? Brushes.White : Brushes.Transparent,
                BorderBrush = isActive ? (Brush)FindResource("Slate200") : Brushes.Transparent,
                BorderThickness = new Thickness(1, 0, 1, 0),
                Cursor = Cursors.Hand,
                Child = new Grid { Children = { inner } }
            };

            // Viền trên màu xanh 2px cho tab đang kích hoạt
            var wrap = new Border
            {
                BorderThickness = new Thickness(0, 2, 0, 0),
                BorderBrush = isActive ? (Brush)FindResource("Blue500") : Brushes.Transparent,
                Child = chip,
                Margin = new Thickness(0, 0, 2, 0)
            };

            wrap.MouseLeftButtonDown += (_, _) => ActivateTab(tab);
            if (!isActive)
            {
                wrap.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x99, 0xE2, 0xE8, 0xF0));
                wrap.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
            }

            tab.Chip = wrap;
            TabStrip.Children.Add(wrap);
        }
    }

    // Chấm emerald "Hệ Thống Sẵn Sàng" nhấp nháy như animate-pulse
    private void StartPulseAnimation()
    {
        var anim = new DoubleAnimation(1.0, 0.25, TimeSpan.FromSeconds(1))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        PulseDot.BeginAnimation(OpacityProperty, anim);
    }
}

/// <summary>Các màn hình cài đặt interface này để được làm mới khi kích hoạt tab / dữ liệu đổi.</summary>
public interface IModuleView
{
    void RefreshView();
}

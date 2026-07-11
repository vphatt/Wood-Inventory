using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WoodInventory.Data;
using WoodInventory.Helpers;
using WoodInventory.Views;

namespace WoodInventory;

/// <summary>
/// Cửa sổ chính: sidebar điều hướng + dải tab động (mở/đóng tab như bản web)
/// + breadcrumb + vùng nội dung + thanh trạng thái.
/// </summary>
public partial class MainWindow : Window
{
    private sealed record NavItem(string Module, string Label, string Glyph, string Group = null);

    private sealed class WorkTab
    {
        public string Module;
        public string Title;
        public Border Chip;          // phần tử hiển thị trên dải tab
    }

    private static readonly NavItem[] NavItems =
    {
        new("dashboard",  "Nav.Dashboard",        ""),
        new("suppliers",  "Nav.Suppliers",           "", "Nav.Group.General"),
        new("categories", "Nav.Categories",         "", "Nav.Group.General"),
        new("receipts",   "Nav.Receipts",            "", "Nav.Group.Inventory"),
        new("lots",       "Nav.Lots", "", "Nav.Group.Inventory"),
        new("issues",     "Nav.Issues",            "", "Nav.Group.Inventory"),
    };

    /// <summary>Ghim riêng ở đáy sidebar (ngoài NavPanel cuộn được) — luôn hiện, không thuộc nhóm nào.</summary>
    private static readonly NavItem SettingsNavItem = new("settings", "Nav.Settings", "");

    private readonly List<WorkTab> _tabs = new();
    private readonly Dictionary<string, UserControl> _viewCache = new();
    private string _activeModule = "dashboard";
    private bool _sidebarCollapsed;

    private const double SidebarExpandedWidth = 256;
    private const double SidebarCollapsedWidth = 68;

    public MainWindow()
    {
        InitializeComponent();

        BtnToggleSidebar.MouseEnter += (_, _) => BtnToggleSidebar.Background = (Brush)FindResource("SideHover");
        BtnToggleSidebar.MouseLeave += (_, _) => BtnToggleSidebar.Background = Brushes.Transparent;

        AppState.Changed += OnDataChanged;
        Unloaded += (_, _) => AppState.Changed -= OnDataChanged;

        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguageChanged;

        UpdateFooterCompanyName();
        OpenModule("dashboard");

        // Hỗ trợ mở thẳng một module khi khởi động: WoodInventory.exe --module lots
        var args = Environment.GetCommandLineArgs();
        var idx = Array.IndexOf(args, "--module");
        if (idx >= 0 && idx + 1 < args.Length)
            OpenModule(args[idx + 1]);
    }

    private void OnDataChanged()
    {
        BuildNav();
        UpdateFooterCompanyName();
        RefreshActiveView();
    }

    private void RefreshActiveView()
    {
        if (_viewCache.TryGetValue(_activeModule, out var view) && view is IModuleView refreshable)
            refreshable.RefreshView();
    }

    /// <summary>Nút làm mới chung trên breadcrumb — làm mới trang đang xem theo yêu cầu, không cần đóng/mở lại tab.</summary>
    private void BtnGlobalRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshActiveView();
        SpinRefreshIcon();
    }

    /// <summary>Icon xoay 1 vòng (360°) rồi dừng để phản hồi trực quan lúc bấm — dùng "By" (cộng dồn từ góc
    /// hiện tại) thay vì đặt lại From=0, để bấm liên tiếp lúc icon đang xoay không bị giật ngược về 0.</summary>
    private void SpinRefreshIcon()
    {
        var anim = new DoubleAnimation
        {
            By = 360,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        RefreshIconRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    /// <summary>
    /// Hot-swap ngôn ngữ: phần XAML tĩnh (Text="{helpers:Loc ...}") tự cập nhật qua binding, nhưng phần
    /// dựng bằng code-behind (nav, tab title, breadcrumb, và bên trong từng View) phải tự rebuild lại —
    /// dùng lại đúng các hàm Build.../RefreshView() sẵn có, giống hệt cơ chế AppState.Changed.
    /// </summary>
    private void OnLanguageChanged()
    {
        foreach (var tab in _tabs) tab.Title = GetModuleTitle(tab.Module);
        var active = _tabs.FirstOrDefault(t => t.Module == _activeModule);
        if (active != null) BreadcrumbCurrent.Text = active.Title;

        BuildNav();
        BuildTabStrip();

        foreach (var view in _viewCache.Values)
            (view as IModuleView)?.RefreshView();
    }

    private void UpdateFooterCompanyName()
    {
        var name = AppState.Settings.CompanyName;
        FooterCompanyName.Text = string.IsNullOrWhiteSpace(name) ? "" : name.ToUpperInvariant();
    }

    private static string GetModuleTitle(string module) => Lang.T(module switch
    {
        "dashboard" => "Nav.Dashboard",
        "categories" => "Nav.Categories",
        "suppliers" => "Nav.Suppliers",
        "lots" => "Nav.Lots",
        "receipts" => "Nav.Receipts",
        "issues" => "Nav.Issues",
        "settings" => "Nav.Settings",
        _ => "Module"
    });

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
        "receipts" => new ReceiptsView(),
        "issues" => new IssuesView(),
        "settings" => new SettingsView(),
        _ => new DashboardView()
    };

    // ---------------- Sidebar ----------------

    /// <summary>Thu nhỏ sidebar chỉ còn icon từng tab (ẩn cả logo app) / mở rộng lại.</summary>
    private void BtnToggleSidebar_Click(object sender, MouseButtonEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumnDef.Width = new GridLength(_sidebarCollapsed ? SidebarCollapsedWidth : SidebarExpandedWidth);
        LogoIcon.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        LogoTextPanel.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarToggleIcon.Text = _sidebarCollapsed ? "" : "";
        // Thu nhỏ: header Padding trái đổi 24→12 + nút neo TRÁI (Padding riêng 12) = icon nút nằm đúng x=24,
        // KHỚP x của icon các tab bên dưới (12 ScrollViewer + 12 Border mỗi hàng nav). Mở rộng: giữ góc phải như cũ.
        SidebarHeader.Padding = new Thickness(_sidebarCollapsed ? 12 : 24, 0, _sidebarCollapsed ? 12 : 24, 0);
        BtnToggleSidebar.HorizontalAlignment = _sidebarCollapsed ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        BuildNav();
    }

    private void BuildNav()
    {
        NavPanel.Children.Clear();

        string lastGroup = null;
        foreach (var item in NavItems)
        {
            // Đổi nhóm → chèn tiêu đề nhóm mới trước mục đầu tiên của nhóm đó (mục không có nhóm, vd Dashboard, thì bỏ qua).
            // Sidebar thu nhỏ vẫn phải giữ NGUYÊN khoảng không gian này (chỉ đổi chữ thành "—" căn giữa) —
            // nếu bỏ hẳn, các mục bên dưới bị đẩy lên, icon lệch vị trí so với lúc mở rộng.
            if (item.Group != lastGroup)
            {
                if (!string.IsNullOrEmpty(item.Group))
                {
                    NavPanel.Children.Add(new TextBlock
                    {
                        Text = _sidebarCollapsed ? "—" : Lang.T(item.Group),
                        Foreground = (Brush)FindResource("Slate500"),
                        FontSize = 10.5, FontWeight = FontWeights.Bold,
                        TextAlignment = _sidebarCollapsed ? TextAlignment.Center : TextAlignment.Left,
                        Margin = new Thickness(12, lastGroup == null ? 8 : 16, 12, 8)
                    });
                }
                lastGroup = item.Group;
            }

            NavPanel.Children.Add(BuildNavRow(item));
        }

        // Cài Đặt ghim riêng ở đáy sidebar (ngoài NavPanel cuộn được, xem SettingsNavHost trong XAML) —
        // dùng CHUNG BuildNavRow nên icon vẫn thẳng cột với các mục bên trên dù nằm ở vùng khác.
        SettingsNavHost.Children.Clear();
        SettingsNavHost.Children.Add(BuildNavRow(SettingsNavItem));
    }

    private Border BuildNavRow(NavItem item)
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
            Text = Lang.T(item.Label),
            FontSize = 12,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = isActive ? Brushes.White : (Brush)FindResource("Slate400"),
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Medium,
            Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible
        };

        // Luôn neo trái (không center khi thu nhỏ) để icon đứng yên 1 chỗ, không "nhảy" vị trí lúc thu nhỏ/mở rộng.
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        left.Children.Add(icon);
        left.Children.Add(label);

        var row = new Grid();
        row.Children.Add(left);

        // Huy hiệu (badge): số kiện gỗ cho mục Lots — ẩn khi sidebar thu nhỏ (không đủ chỗ).
        if (!_sidebarCollapsed && item.Module == "lots")
        {
            var lowStock = AppState.LowStockCount > 0;
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
                    Text = AppState.Lots.Count.ToString(),
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
            Padding = new Thickness(12, 8, 12, 8), // giữ cố định cả 2 trạng thái để icon không đổi vị trí
            Margin = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            Child = row,
            Tag = item.Module,
            ToolTip = _sidebarCollapsed ? Lang.T(item.Label) : null
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

        return border;
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

}

/// <summary>Các màn hình cài đặt interface này để được làm mới khi kích hoạt tab / dữ liệu đổi.</summary>
public interface IModuleView
{
    void RefreshView();
}

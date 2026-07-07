using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TimberFlowDesktop.Data;
using TimberFlowDesktop.Domain;

namespace TimberFlowDesktop.Views;

public partial class WoodCategoriesView : UserControl, IModuleView
{
    /// <summary>Dòng dữ liệu cho DataGrid.</summary>
    public sealed class CatRow
    {
        public WoodCategory Category { get; }
        public string Name => Category.Name;
        public string RuleLabel => Category.VolumeRuleLabel;
        public bool IsFootage => Category.VolumeRule == VolumeRule.ByFootage;
        public CatRow(WoodCategory c) => Category = c;
    }

    // null = đang thêm mới; có giá trị = đang xem/sửa loại gỗ có Id này
    private string _editingId;
    private string _mode = "add";   // add | view | edit
    private readonly List<CatRow> _rows = new();
    private ICollectionView _view;

    public WoodCategoriesView()
    {
        InitializeComponent();
        InitRuleCombo();
        InitFilterCombo();
        RefreshView();
        Helpers.GridLayoutStore.Attach(Grid, "categories");
    }

    private void InitFilterCombo()
    {
        FilterRule.Items.Add(new ComboBoxItem { Content = "Tất cả nguyên tắc", Tag = "ALL", IsSelected = true });
        FilterRule.Items.Add(new ComboBoxItem { Content = "Theo quy cách", Tag = "SPEC" });
        FilterRule.Items.Add(new ComboBoxItem { Content = "Theo Footage", Tag = "FOOT" });
    }

    private void InitRuleCombo()
    {
        FRule.Items.Add(new ComboBoxItem
        {
            Content = "Theo quy cách (Dày x Rộng x Dài)",
            Tag = VolumeRule.BySpecification,
            IsSelected = true
        });
        FRule.Items.Add(new ComboBoxItem
        {
            Content = "Theo Footage",
            Tag = VolumeRule.ByFootage
        });
        FRule.SelectionChanged += (_, _) => UpdateRuleHint();
        UpdateRuleHint();
    }

    private VolumeRule SelectedRule =>
        (VolumeRule)((FRule.SelectedItem as ComboBoxItem)?.Tag ?? VolumeRule.BySpecification);

    private void SelectRule(VolumeRule rule)
    {
        foreach (ComboBoxItem item in FRule.Items)
            if ((VolumeRule)item.Tag == rule) { FRule.SelectedItem = item; return; }
    }

    private void UpdateRuleHint()
    {
        if (RuleHint == null) return;
        RuleHint.Text = SelectedRule == VolumeRule.ByFootage
            ? "Khi nhập/xuất loại gỗ này, hệ thống tính m³ theo Footage — bắt buộc nhập Độ dày và Footage (không cần Rộng/Dài)."
            : "Khi nhập/xuất loại gỗ này, hệ thống tính m³ theo quy cách — bắt buộc nhập đầy đủ Độ dày, Chiều rộng và Chiều dài.";
    }

    public void RefreshView()
    {
        _rows.Clear();
        foreach (var c in AppState.Categories) _rows.Add(new CatRow(c));

        if (_view == null)
        {
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterPredicate;
            Grid.ItemsSource = _view;
        }
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    private bool FilterPredicate(object o)
    {
        var row = (CatRow)o;
        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        if (term.Length > 0 && !row.Name.ToLowerInvariant().Contains(term)) return false;

        var rule = (FilterRule.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        if (rule == "SPEC" && row.IsFootage) return false;
        if (rule == "FOOT" && !row.IsFootage) return false;
        return true;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    private void UpdateCountAndEmpty()
    {
        var n = _view.Cast<object>().Count();
        TotalCount.Text = n.ToString();
        EmptyRow.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CatRow row) EnterViewMode(row.Category);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CatRow row) DeleteCategory(row.Category);
    }

    // ---------------- Thêm / Sửa ----------------

    /// <summary>Bật/tắt chế độ chỉ-đọc cho các ô nhập trong form.</summary>
    private void SetReadOnly(bool ro)
    {
        FName.IsReadOnly = ro;
        FName.Background = ro ? (Brush)FindResource("Slate50") : Brushes.White;
        FRule.IsEnabled = !ro;
    }

    private void EnterAddMode()
    {
        _mode = "add";
        _editingId = null;
        ClearWarnings();
        SetReadOnly(false);
        FormTitle.Text = "Khai Báo Loại Gỗ Mới";
        FormSaveBtn.Content = "Lưu loại gỗ";
        FName.Text = "";
        SelectRule(VolumeRule.BySpecification);
    }

    /// <summary>Xem chi tiết: đẩy form lên như edit nhưng chỉ-đọc, nút thành "Chỉnh sửa".</summary>
    private void EnterViewMode(WoodCategory cat)
    {
        _mode = "view";
        _editingId = cat.Id;
        FName.Text = cat.Name;
        SelectRule(cat.VolumeRule);
        SetReadOnly(true);
        FormTitle.Text = $"Chi Tiết Loại Gỗ — {cat.Name}";
        FormSaveBtn.Content = "Chỉnh sửa";
        AddFormPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Chuyển từ xem sang sửa: mở khóa ô nhập, nút thành "Cập nhật".</summary>
    private void EnterEditMode()
    {
        _mode = "edit";
        ClearWarnings();
        SetReadOnly(false);
        FormTitle.Text = $"Sửa Loại Gỗ — {FName.Text}";
        FormSaveBtn.Content = "Cập nhật";
        FName.Focus();
        FName.SelectAll();
    }

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        // Đang mở sẵn ở chế độ thêm mới → bấm lần nữa thì đóng
        if (AddFormPanel.Visibility == Visibility.Visible && _mode == "add")
        {
            AddFormPanel.Visibility = Visibility.Collapsed;
            return;
        }
        // Còn lại (đang ẩn, hoặc đang xem/sửa) → chuyển thẳng sang thêm mới, xóa nội dung cũ
        EnterAddMode();
        AddFormPanel.Visibility = Visibility.Visible;
    }

    // ---------------- Cảnh báo inline ----------------

    private static void ShowWarn(TextBlock w, string msg)
    {
        w.Text = msg;
        w.Visibility = Visibility.Visible;
    }

    private void ClearWarnings() => WName.Visibility = Visibility.Collapsed;

    private void FName_Changed(object sender, TextChangedEventArgs e)
    {
        if (WName != null) WName.Visibility = Visibility.Collapsed;
    }

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
    {
        AddFormPanel.Visibility = Visibility.Collapsed;
        EnterAddMode();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // Đang xem chi tiết → bấm "Chỉnh sửa" thì chuyển sang chế độ sửa
        if (_mode == "view") { EnterEditMode(); return; }

        ClearWarnings();
        var name = (FName.Text ?? "").Trim();
        if (name.Length == 0)
        {
            ShowWarn(WName, "Vui lòng nhập tên loại gỗ.");
            return;
        }

        try
        {
            if (_editingId == null)
            {
                AppState.AddCategory(new WoodCategory
                {
                    Id = $"CAT-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    Name = name,
                    VolumeRule = SelectedRule
                });
            }
            else
            {
                AppState.UpdateCategory(_editingId, name, SelectedRule);
            }

            AddFormPanel.Visibility = Visibility.Collapsed;
            EnterAddMode();
        }
        catch (Exception ex)
        {
            ShowWarn(WName, ex.Message);   // vd trùng tên → cảnh báo ngay dưới field
        }
    }

    private void DeleteCategory(WoodCategory cat)
    {
        var confirm = MessageBox.Show($"Bạn có chắc muốn xóa loại gỗ \"{cat.Name}\" khỏi danh mục?",
            "TimberFlow ERP", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            AppState.DeleteCategory(cat.Id);
            if (_editingId == cat.Id) { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

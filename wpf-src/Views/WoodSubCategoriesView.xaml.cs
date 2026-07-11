using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WoodInventory.Data;
using WoodInventory.Domain;
using WoodInventory.Helpers;

namespace WoodInventory.Views;

/// <summary>
/// Trang quản lý phân loại con (cấp 2) của một loại gỗ cha. Điều hướng vào từ
/// <see cref="WoodCategoriesView"/> (breadcrump: Phân Loại Gỗ / Tên loại cha).
/// </summary>
public partial class WoodSubCategoriesView : UserControl
{
    public sealed class SubRow
    {
        public WoodSubCategory Sub { get; }
        public string Name => Sub.Name;
        public SubRow(WoodSubCategory s) => Sub = s;
    }

    private readonly WoodCategory _category;
    private readonly Action _back;

    private string _editingId;
    private string _mode = "add";   // add | view | edit
    private readonly List<SubRow> _rows = new();
    private ICollectionView _view;

    public WoodSubCategoriesView(WoodCategory category, Action back)
    {
        InitializeComponent();
        _category = category;
        _back = back;
        TitleName.Text = Lang.T("WoodSubCategories.TitleName", category.Name);
        Subtitle.Text = Lang.T("WoodSubCategories.Subtitle", category.VolumeRuleLabel);
        RebuildList();
        Helpers.GridLayoutStore.Attach(SubGrid, "wood-subcategories");
        Helpers.GridPairSync.Link(SubGrid, ActionGrid);
    }

    public void RefreshView() => RebuildList();

    private void BtnBack_Click(object sender, RoutedEventArgs e) => _back?.Invoke();

    private void RebuildList()
    {
        _rows.Clear();
        foreach (var s in AppState.SubCategoriesOf(_category.Id)) _rows.Add(new SubRow(s));

        if (_view == null)
        {
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterPredicate;
            SubGrid.ItemsSource = _view;
            ActionGrid.ItemsSource = _view;   // cột thao tác tách riêng, cùng nguồn
        }
        _view.Refresh();
        UpdateCountAndEmpty();
    }

    private bool FilterPredicate(object o)
    {
        var r = (SubRow)o;
        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        return term.Length == 0 || (r.Name ?? "").ToLowerInvariant().Contains(term);
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

    // ---------------- Thao tác trên bảng ----------------

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SubRow r) EnterViewMode(r.Sub);
    }

    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SubRow r) { EnterViewMode(r.Sub); EnterEditMode(); }
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SubRow r) return;
        if (MessageBox.Show(Lang.T("WoodSubCategories.Confirm.Delete", r.Name, _category.Name),
                Lang.T("Common.ConfirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            AppState.DeleteSubCategory(r.Sub.Id);
            if (_editingId == r.Sub.Id) { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Lang.T("Common.CannotDeleteTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------- Thêm / Xem / Sửa ----------------

    private void SetReadOnly(bool ro)
    {
        FName.IsReadOnly = ro;
        FName.Background = ro ? (Brush)FindResource("Slate50") : Brushes.White;
    }

    private void EnterAddMode()
    {
        _mode = "add";
        _editingId = null;
        ClearWarnings();
        SetReadOnly(false);
        FormTitle.Text = Lang.T("WoodSubCategories.Form.AddTitle");
        FormSaveBtn.Content = Lang.T("WoodSubCategories.SaveButton");
        FormCancelBtn.Content = Lang.T("Common.Cancel");
        FName.Text = "";
    }

    private void EnterViewMode(WoodSubCategory s)
    {
        _mode = "view";
        _editingId = s.Id;
        ClearWarnings();
        FName.Text = s.Name;
        SetReadOnly(true);
        FormTitle.Text = Lang.T("WoodSubCategories.Form.ViewTitle", s.Name);
        FormSaveBtn.Content = Lang.T("Common.Edit");
        FormCancelBtn.Content = Lang.T("Common.Close");
        AddFormPanel.Visibility = Visibility.Visible;
    }

    private void EnterEditMode()
    {
        _mode = "edit";
        ClearWarnings();
        SetReadOnly(false);
        FormTitle.Text = Lang.T("WoodSubCategories.Form.EditTitle", FName.Text);
        FormSaveBtn.Content = Lang.T("Common.Update");
        FormCancelBtn.Content = Lang.T("Common.CancelEdit");
        FName.Focus();
        FName.SelectAll();
    }

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        if (AddFormPanel.Visibility == Visibility.Visible && _mode == "add")
        {
            AddFormPanel.Visibility = Visibility.Collapsed;
            return;
        }
        EnterAddMode();
        AddFormPanel.Visibility = Visibility.Visible;
    }

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
    {
        // Đang sửa → xác nhận hủy, bỏ thay đổi và quay lại xem chi tiết (không lưu)
        if (_mode == "edit")
        {
            if (!ConfirmDiscard(Lang.T("Common.Confirm.DiscardEdit"))) return;
            var s = AppState.SubCategories.FirstOrDefault(x => x.Id == _editingId);
            if (s != null) { EnterViewMode(s); return; }
        }
        // Đang thêm mới → xác nhận trước khi bỏ thông tin đã nhập
        else if (_mode == "add")
        {
            if (!ConfirmDiscard(Lang.T("Common.Confirm.DiscardAdd"))) return;
        }
        AddFormPanel.Visibility = Visibility.Collapsed;
        EnterAddMode();
    }

    /// <summary>Hộp thoại xác nhận hủy (thông điệp tùy chế độ add/edit).</summary>
    private static bool ConfirmDiscard(string message) =>
        MessageBox.Show(message, Lang.T("Common.ConfirmDiscardTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // Đang xem chi tiết → bấm "Chỉnh sửa" thì chuyển sang chế độ sửa
        if (_mode == "view") { EnterEditMode(); return; }

        ClearWarnings();
        if (string.IsNullOrWhiteSpace(FName.Text))
        {
            ShowWarn(WName, Lang.T("WoodSubCategories.Warn.Name"));
            return;
        }

        try
        {
            if (_mode == "edit") AppState.UpdateSubCategory(_editingId, FName.Text);
            else AppState.AddSubCategory(_category.Id, FName.Text);
            AddFormPanel.Visibility = Visibility.Collapsed;
            EnterAddMode();
        }
        catch (Exception ex)
        {
            ShowWarn(WName, ex.Message);
        }
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
}

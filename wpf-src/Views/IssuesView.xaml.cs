using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WoodInventory.Data;
using WoodInventory.Domain;
using WoodInventory.Helpers;

namespace WoodInventory.Views;

public partial class IssuesView : UserControl, IModuleView
{
    /// <summary>Một dòng xuất kho đang soạn.</summary>
    private sealed class DraftItem
    {
        public string WoodLotId = "";
        public string Quantity = "10";

        // Chỉ có giá trị khi dòng này nạp lại từ một WarehouseIssueItem đã lưu (chế độ edit) — dùng để
        // "cộng lại" phần đã xuất của chính dòng này vào tồn khả dụng khi validate (DB chưa hoàn trả thật).
        public string OriginalWoodLotId;
        public int OriginalQuantity;
        public double OriginalCbm;

        // Kết quả kiểm tra/tính toán gần nhất
        public WoodLot Lot;
        public bool IsValid;
        public string Error;
        public double Cbm;
        public decimal CostPriceVnd;
        public decimal TotalValueVnd;
        public int AvailableQty;      // tồn khả dụng đã tính cộng lại phần của chính dòng này (nếu edit)
        public double AvailableCbm;
    }

    private readonly List<DraftItem> _draftItems = new();

    private string _mode = "add";          // add | view | edit
    private string _editingIssueId;
    private bool ReadOnly => _mode == "view";

    public IssuesView()
    {
        InitializeComponent();
        FDate.SelectedDate = DateTime.Today;
        ResetDraft();
        RefreshView();
        Helpers.GridLayoutStore.Attach(HistoryGrid, "issues");
        Helpers.GridPairSync.Link(HistoryGrid, ActionGrid);
    }

    private void ResetDraft()
    {
        _draftItems.Clear();
        _draftItems.Add(new DraftItem());
        RebuildRows();
    }

    public void RefreshView()
    {
        var current = (FOrder.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        FOrder.Items.Clear();
        FOrder.Items.Add(new ComboBoxItem { Content = "-- Chọn Đơn Hàng --", Tag = "" });
        foreach (var o in AppState.Orders)
        {
            var statusText = o.Status == "processing" ? "Đang sản xuất" : "Chờ xử lý";
            FOrder.Items.Add(new ComboBoxItem { Content = $"{o.Id} - {o.CustomerName} ({statusText})", Tag = o.Id });
        }
        foreach (ComboBoxItem item in FOrder.Items)
            if ((item.Tag as string) == current) FOrder.SelectedItem = item;
        if (FOrder.SelectedItem == null) FOrder.SelectedIndex = 0;

        RebuildRows();
        RebuildHistory();
    }

    private static double D(string s) => Fmt.ParseNum(s);

    /// <summary>
    /// Tính toán/kiểm tra một dòng xuất — logic khớp bản web. Khi đang SỬA một phiếu đã lưu và dòng này
    /// vẫn giữ nguyên kiện gốc, tồn khả dụng phải CỘNG LẠI phần đã xuất của chính dòng này — DB chưa thật
    /// sự hoàn trả (chỉ hoàn trả lúc bấm Lưu, xem <see cref="AppState.UpdateIssue"/>), nếu không sẽ báo lỗi
    /// sai "vượt quá tồn kho" ngay cả khi giữ nguyên số lượng cũ.
    /// </summary>
    private static void Recalculate(DraftItem item)
    {
        item.Lot = AppState.Lots.FirstOrDefault(l => l.Id == item.WoodLotId);
        item.Cbm = 0;
        item.TotalValueVnd = 0;

        if (item.Lot == null)
        {
            item.IsValid = false;
            item.Error = "Vui lòng chọn kiện gỗ";
            item.CostPriceVnd = 0;
            item.AvailableQty = 0;
            item.AvailableCbm = 0;
            return;
        }
        item.CostPriceVnd = item.Lot.CostPriceVnd;

        var sameOriginalLot = item.OriginalWoodLotId != null && item.OriginalWoodLotId == item.WoodLotId;
        item.AvailableQty = item.Lot.Quantity + (sameOriginalLot ? item.OriginalQuantity : 0);
        item.AvailableCbm = Math.Min(item.Lot.Cbm, item.Lot.RemainingCbm + (sameOriginalLot ? item.OriginalCbm : 0));

        var qty = (int)D(item.Quantity);
        if (qty <= 0)
        {
            item.IsValid = false;
            item.Error = "Số lượng phải lớn hơn 0";
            return;
        }
        if (qty > item.AvailableQty)
        {
            item.IsValid = false;
            item.Error = $"Số lượng vượt quá tồn kho khả dụng ({item.AvailableQty})";
            return;
        }

        double cbm;
        if (AppState.GetVolumeRule(item.Lot.WoodType) == VolumeRule.ByFootage)
        {
            var proportionateFootage = (double)qty / item.Lot.OriginalQuantity * item.Lot.Footage;
            cbm = Math.Round(proportionateFootage / 1000.0 * 2.36, 4);
        }
        else
        {
            cbm = Math.Round(item.Lot.ThicknessMm * item.Lot.WidthMm * item.Lot.LengthMm * qty / 1_000_000_000.0, 4);
        }
        if (cbm > item.AvailableCbm) cbm = item.AvailableCbm;

        item.Cbm = cbm;
        item.TotalValueVnd = Math.Round(item.Lot.CostPriceVnd * (decimal)cbm, 0);
        item.IsValid = true;
        item.Error = null;
    }

    // ---------------- Bảng soạn phiếu ----------------

    private void RebuildRows()
    {
        IssueRowsPanel.Items.Clear();
        for (var i = 0; i < _draftItems.Count; i++)
            IssueRowsPanel.Items.Add(_mode == "view" ? BuildRowReadOnly(_draftItems[i], i + 1) : BuildRow(_draftItems[i], i + 1));
        UpdateTotalsAndErrors();
    }

    /// <summary>Chế độ xem: danh sách dòng xuất hiển thị thuần bảng đọc (TextBlock), không phải field form.</summary>
    private FrameworkElement BuildRowReadOnly(DraftItem item, int stt)
    {
        Recalculate(item);

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        foreach (var w in new[] { 45.0, 220, -1, 150, 100, 115, 120, 120, 50 })
            grid.ColumnDefinitions.Add(w < 0
                ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 }
                : new ColumnDefinition { Width = new GridLength(w) });

        void Cell(string text, int col, HorizontalAlignment align, bool mono = true,
            Brush color = null, FontWeight? weight = null, Thickness? margin = null)
        {
            var tb = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(text) ? "-" : text,
                Foreground = color ?? (Brush)FindResource("Slate700"),
                HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
                Margin = margin ?? new Thickness(6, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (mono) tb.FontFamily = (FontFamily)FindResource("FontMono");
            if (weight.HasValue) tb.FontWeight = weight.Value;
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        Cell(stt.ToString(), 0, HorizontalAlignment.Center, color: (Brush)FindResource("Slate400"));
        Cell(item.WoodLotId, 1, HorizontalAlignment.Left, weight: FontWeights.SemiBold,
            color: (Brush)FindResource("Slate900"), margin: new Thickness(12, 0, 6, 0));
        Cell(item.Lot != null
            ? $"{item.Lot.WoodName} — Grade: {item.Lot.Grade} • {Fmt.Num(item.Lot.ThicknessMm)}x{Fmt.Num(item.Lot.WidthMm)}x{Fmt.Num(item.Lot.LengthMm)}mm"
            : "-", 2, HorizontalAlignment.Left, mono: false);
        Cell(item.Lot != null ? $"{item.AvailableQty} thanh / {Fmt.M3Short(item.AvailableCbm)} m³" : "-", 3, HorizontalAlignment.Center);
        Cell(item.Quantity + " thanh", 4, HorizontalAlignment.Center);
        Cell($"{Fmt.M3(item.Cbm)} m³", 5, HorizontalAlignment.Right, color: (Brush)FindResource("Blue600"), weight: FontWeights.Medium);
        Cell(item.Lot != null ? Fmt.Vnd(item.CostPriceVnd) : "-", 6, HorizontalAlignment.Right, color: (Brush)FindResource("Slate500"));
        Cell(item.TotalValueVnd > 0 ? Fmt.Vnd(item.TotalValueVnd) : "-", 7, HorizontalAlignment.Right,
            weight: FontWeights.SemiBold, color: (Brush)FindResource("Rose600"), margin: new Thickness(6, 0, 12, 0));
        // Cột 8 (Xóa): để trống — chế độ xem không cho xóa.

        return new Border
        {
            BorderBrush = (Brush)FindResource("Slate100"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.White,
            Child = grid
        };
    }

    private FrameworkElement BuildRow(DraftItem item, int stt)
    {
        Recalculate(item);
        // Đang sửa và dòng này giữ kiện gốc → vẫn phải hiện được trong combo dù Quantity hiện tại = 0
        // (chưa hoàn trả thật trong DB, xem Recalculate).
        var availableLots = AppState.Lots.Where(l => l.Quantity > 0 || l.Id == item.OriginalWoodLotId).ToList();

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        foreach (var w in new[] { 45.0, 220, -1, 150, 100, 115, 120, 120, 50 })
            grid.ColumnDefinitions.Add(w < 0
                ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 }
                : new ColumnDefinition { Width = new GridLength(w) });

        var sttText = new TextBlock
        {
            Text = stt.ToString(), Foreground = (Brush)FindResource("Slate400"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(sttText, 0); grid.Children.Add(sttText);

        // Các ô hiển thị (khai báo trước để handler cập nhật)
        var infoPanel = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var availText = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("FontMono"), Foreground = (Brush)FindResource("Slate700"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var cbmText = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("FontMono"), FontWeight = FontWeights.Medium,
            Foreground = (Brush)FindResource("Blue600"), Margin = new Thickness(6, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        var costText = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("FontMono"), Foreground = (Brush)FindResource("Slate500"),
            Margin = new Thickness(6, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        var totalText = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("FontMono"), FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Rose600"), Margin = new Thickness(6, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };

        void UpdateCells()
        {
            Recalculate(item);
            infoPanel.Children.Clear();
            if (item.Lot != null)
            {
                infoPanel.Children.Add(new TextBlock
                {
                    Text = item.Lot.WoodName, FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("Slate800"), TextTrimming = TextTrimming.CharacterEllipsis
                });
                infoPanel.Children.Add(new TextBlock
                {
                    Text = $"Grade: {item.Lot.Grade} • Kích thước: {Fmt.Num(item.Lot.ThicknessMm)}x{Fmt.Num(item.Lot.WidthMm)}x{Fmt.Num(item.Lot.LengthMm)}mm",
                    FontSize = 10, Foreground = (Brush)FindResource("Slate400"), Margin = new Thickness(0, 2, 0, 0)
                });
            }
            else
            {
                infoPanel.Children.Add(new TextBlock
                {
                    Text = "-", FontFamily = (FontFamily)FindResource("FontMono"),
                    Foreground = (Brush)FindResource("Slate400")
                });
            }
            availText.Text = item.Lot != null
                ? $"{item.AvailableQty} thanh / {Fmt.M3Short(item.AvailableCbm)} m³" : "-";
            cbmText.Text = $"{Fmt.M3(item.Cbm)} m³";
            costText.Text = item.Lot != null ? Fmt.Vnd(item.CostPriceVnd) : "-";
            totalText.Text = item.TotalValueVnd > 0 ? Fmt.Vnd(item.TotalValueVnd) : "-";
            UpdateTotalsAndErrors();
        }

        // Combo chọn kiện
        var lotCombo = new ComboBox
        {
            Style = (Style)FindResource("Select"),
            FontFamily = (FontFamily)FindResource("FontMono"),
            Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
        };
        lotCombo.Items.Add(new ComboBoxItem { Content = "-- Chọn Kiện gỗ --", Tag = "", IsSelected = item.WoodLotId == "" });
        foreach (var lot in availableLots)
            lotCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{lot.Id} ({lot.WoodType}{(string.IsNullOrWhiteSpace(lot.WoodSubType) ? "" : " · " + lot.WoodSubType)} - {lot.Quantity} thanh còn)",
                Tag = lot.Id,
                IsSelected = lot.Id == item.WoodLotId
            });
        lotCombo.SelectionChanged += (_, _) =>
        {
            item.WoodLotId = (lotCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            UpdateCells();
        };
        Grid.SetColumn(lotCombo, 1); grid.Children.Add(lotCombo);

        Grid.SetColumn(infoPanel, 2); grid.Children.Add(infoPanel);
        Grid.SetColumn(availText, 3); grid.Children.Add(availText);

        // Số lượng xuất
        var qtyBox = new TextBox
        {
            Style = (Style)FindResource("CellInputMono"),
            Text = item.Quantity, TextAlignment = TextAlignment.Center,
            Width = 80, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        qtyBox.TextChanged += (_, _) => { item.Quantity = qtyBox.Text; UpdateCells(); };
        Grid.SetColumn(qtyBox, 4); grid.Children.Add(qtyBox);

        Grid.SetColumn(cbmText, 5); grid.Children.Add(cbmText);
        Grid.SetColumn(costText, 6); grid.Children.Add(costText);
        Grid.SetColumn(totalText, 7); grid.Children.Add(totalText);

        var del = new Button
        {
            Style = (Style)FindResource("BtnIconDanger"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12 }
        };
        del.Click += (_, _) =>
        {
            _draftItems.Remove(item);
            RebuildRows();
        };
        Grid.SetColumn(del, 8); grid.Children.Add(del);

        UpdateCells();

        return new Border
        {
            BorderBrush = (Brush)FindResource("Slate100"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.White,
            Child = grid
        };
    }

    private void UpdateTotalsAndErrors()
    {
        TotalCbm.Text = $"{Fmt.M3(_draftItems.Sum(i => i.Cbm))} m³";
        TotalValue.Text = Fmt.Vnd(_draftItems.Sum(i => i.TotalValueVnd));

        ErrorList.Children.Clear();
        var hasError = false;
        for (var i = 0; i < _draftItems.Count; i++)
        {
            if (_draftItems[i].Error == null) continue;
            hasError = true;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new Border
            {
                Width = 6, Height = 6, CornerRadius = new CornerRadius(3),
                Background = (Brush)FindResource("Rose500"), VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = $"Dòng #{i + 1}: {_draftItems[i].Error}",
                Foreground = (Brush)FindResource("Rose600"), FontWeight = FontWeights.Medium,
                Margin = new Thickness(8, 0, 0, 0)
            });
            ErrorList.Children.Add(row);
        }
        ErrorPanel.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnAddRow_Click(object sender, RoutedEventArgs e)
    {
        _draftItems.Add(new DraftItem());
        RebuildRows();
    }

    // ---------------- Chế độ add / view / edit ----------------

    private void SetHeaderReadOnly(bool ro)
    {
        FOrder.IsEnabled = !ro;
        FDate.IsEnabled = !ro;
    }

    private void EnterAddMode()
    {
        _mode = "add";
        _editingIssueId = null;
        SetHeaderReadOnly(false);
        BtnAddRow.Visibility = Visibility.Visible;
        FormTitle.Text = "Tạo Phiếu Xuất Kho Mới";
        FormSaveBtn.Content = "Xác nhận xuất kho sản xuất";
        FormCancelBtn.Content = "Hủy bỏ";
        FDate.SelectedDate = DateTime.Today;
        if (FOrder.Items.Count > 0) FOrder.SelectedIndex = 0;
        ResetDraft();
    }

    /// <summary>Xem chi tiết: nạp dữ liệu phiếu vào form ở chế độ chỉ-đọc, nút thành "Chỉnh sửa".</summary>
    private void EnterViewMode(WarehouseIssue i)
    {
        _mode = "view";
        _editingIssueId = i.Id;
        LoadIssueIntoForm(i);
        SetHeaderReadOnly(true);
        BtnAddRow.Visibility = Visibility.Collapsed;
        FormTitle.Text = $"Chi Tiết Phiếu Xuất — {i.Id}";
        FormSaveBtn.Content = "Chỉnh sửa";
        FormCancelBtn.Content = "Đóng";
        AddFormPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Chuyển sang sửa (từ xem hoặc trực tiếp): mở khóa, nút thành "Cập nhật".</summary>
    private void EnterEditMode(WarehouseIssue i = null)
    {
        _mode = "edit";
        if (i != null) { _editingIssueId = i.Id; LoadIssueIntoForm(i); }
        SetHeaderReadOnly(false);
        RebuildRows();
        BtnAddRow.Visibility = Visibility.Visible;
        FormTitle.Text = $"Sửa Phiếu Xuất — {_editingIssueId}";
        FormSaveBtn.Content = "Cập nhật";
        FormCancelBtn.Content = "Hủy sửa";
        AddFormPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Nạp header + danh sách dòng xuất của một phiếu vào form.</summary>
    private void LoadIssueIntoForm(WarehouseIssue i)
    {
        foreach (ComboBoxItem it in FOrder.Items)
            if ((it.Tag as string) == i.OrderId) { FOrder.SelectedItem = it; break; }
        FDate.SelectedDate = i.Date;

        _draftItems.Clear();
        foreach (var it in i.Items) _draftItems.Add(ToDraft(it));
        if (_draftItems.Count == 0) _draftItems.Add(new DraftItem());
        RebuildRows();
    }

    private static DraftItem ToDraft(WarehouseIssueItem i) => new()
    {
        WoodLotId = i.WoodLotId,
        OriginalWoodLotId = i.WoodLotId,
        Quantity = i.Quantity.ToString(),
        OriginalQuantity = i.Quantity,
        OriginalCbm = i.Cbm
    };

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        // Đang mở sẵn ở chế độ thêm mới → bấm lần nữa thì đóng
        if (AddFormPanel.Visibility == Visibility.Visible && _mode == "add")
        {
            AddFormPanel.Visibility = Visibility.Collapsed;
            return;
        }
        // Còn lại (đang ẩn, hoặc đang xem/sửa) → chuyển thẳng sang lập phiếu mới
        EnterAddMode();
        AddFormPanel.Visibility = Visibility.Visible;
    }

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
    {
        // Đang sửa → xác nhận hủy, bỏ thay đổi và quay lại xem chi tiết (không lưu)
        if (_mode == "edit")
        {
            if (!ConfirmDiscard("Những thay đổi sẽ không được lưu, tiếp tục huỷ?")) return;
            var i = AppState.Issues.FirstOrDefault(x => x.Id == _editingIssueId);
            if (i != null) { EnterViewMode(i); return; }
        }
        // Đang thêm mới → xác nhận trước khi bỏ thông tin đã nhập
        else if (_mode == "add")
        {
            if (!ConfirmDiscard("Các thông tin chưa được lưu, tiếp tục huỷ?")) return;
        }
        AddFormPanel.Visibility = Visibility.Collapsed;
        EnterAddMode();
    }

    /// <summary>Hộp thoại xác nhận hủy (thông điệp tùy chế độ add/edit).</summary>
    private static bool ConfirmDiscard(string message) =>
        MessageBox.Show(message, "Xác nhận hủy",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void BtnSaveIssue_Click(object sender, RoutedEventArgs e)
    {
        // Đang xem chi tiết → bấm "Chỉnh sửa" thì chuyển sang chế độ sửa
        if (_mode == "view") { EnterEditMode(); return; }

        var orderId = (FOrder.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        if (orderId.Length == 0)
        {
            MessageBox.Show("Vui lòng chọn hoặc lập Đơn hàng sản xuất.", "Quản Lý Gỗ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var item in _draftItems) Recalculate(item);
        var invalid = _draftItems.FirstOrDefault(i => !i.IsValid);
        if (invalid != null)
        {
            MessageBox.Show($"Lỗi danh sách xuất kho: {invalid.Error}. Vui lòng kiểm tra lại.",
                "Quản Lý Gỗ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var date = FDate.SelectedDate ?? DateTime.Today;

        var issueId = _mode == "edit" ? _editingIssueId : $"ISS-{Random.Shared.Next(10000, 99999)}";
        var issue = new WarehouseIssue
        {
            Id = issueId,
            OrderId = orderId,
            Date = date
        };
        foreach (var d in _draftItems)
        {
            issue.Items.Add(new WarehouseIssueItem
            {
                WoodLotId = d.WoodLotId,
                Quantity = (int)D(d.Quantity),
                Cbm = d.Cbm,
                CostPriceVnd = d.CostPriceVnd
            });
        }

        try
        {
            if (_mode == "edit") AppState.UpdateIssue(issue);
            else AppState.AddIssue(issue);
            var saved = AppState.Issues.FirstOrDefault(i => i.Id == issueId);
            if (saved != null) EnterViewMode(saved);
            else { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------- Xử lý nút thao tác trên bảng ----------------

    private void ViewRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is IssRow r) EnterViewMode(r.Issue);
    }

    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is IssRow r) EnterEditMode(r.Issue);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IssRow r) return;
        if (MessageBox.Show(
                $"Xóa phiếu xuất {r.Id} ({r.Qty} thanh)? Tồn kho các kiện liên quan sẽ được hoàn trả.\nHành động này không thể hoàn tác.",
                "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            AppState.DeleteIssue(r.Id);
            if (_editingIssueId == r.Id) { AddFormPanel.Visibility = Visibility.Collapsed; EnterAddMode(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------- Lịch sử (DataGrid + tìm kiếm / lọc) ----------------

    public sealed class IssRow
    {
        public WarehouseIssue Issue { get; }
        public string Id => Issue.Id;
        public string OrderId => Issue.OrderId;
        public string CustomerName { get; }
        public string DateText => Fmt.Date(Issue.Date);
        public List<string> LotIds { get; }
        public int Qty { get; }
        public string QtyText => $"{Qty} thanh";
        public double Vol { get; }
        public string VolText => $"{Fmt.M3(Vol)} m³";
        public decimal Val { get; }
        public string ValText => Fmt.Vnd(Val);
        public IssRow(WarehouseIssue i)
        {
            Issue = i;
            CustomerName = AppState.Orders.FirstOrDefault(o => o.Id == i.OrderId)?.CustomerName ?? "";
            LotIds = i.Items.Select(x => x.WoodLotId).ToList();
            Qty = i.Items.Sum(x => x.Quantity);
            Vol = i.Items.Sum(x => x.Cbm);
            Val = i.Items.Sum(x => x.CostPriceVnd * (decimal)x.Cbm);
        }
    }

    private readonly List<IssRow> _issRows = new();
    private ICollectionView _issView;

    private void PopulateOrderFilter()
    {
        var current = (FilterOrder.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        FilterOrder.Items.Clear();
        FilterOrder.Items.Add(new ComboBoxItem { Content = "Tất cả đơn hàng", Tag = "ALL" });
        foreach (var o in AppState.Orders)
            FilterOrder.Items.Add(new ComboBoxItem { Content = $"{o.Id} - {o.CustomerName}", Tag = o.Id });
        foreach (ComboBoxItem it in FilterOrder.Items)
            if ((it.Tag as string) == current) { FilterOrder.SelectedItem = it; break; }
        if (FilterOrder.SelectedItem == null) FilterOrder.SelectedIndex = 0;
    }

    private void RebuildHistory()
    {
        PopulateOrderFilter();
        _issRows.Clear();
        foreach (var i in AppState.Issues) _issRows.Add(new IssRow(i));

        if (_issView == null)
        {
            _issView = CollectionViewSource.GetDefaultView(_issRows);
            _issView.Filter = HistoryFilter;
            HistoryGrid.ItemsSource = _issView;
            ActionGrid.ItemsSource = _issView;   // cột thao tác tách riêng, cùng nguồn
        }
        _issView.Refresh();
        UpdateEmpty();
    }

    private bool HistoryFilter(object o)
    {
        var r = (IssRow)o;
        var ord = (FilterOrder.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
        if (ord != "ALL" && r.OrderId != ord) return false;

        var term = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        var matchSearch = term.Length == 0
            || r.Id.ToLowerInvariant().Contains(term)
            || r.OrderId.ToLowerInvariant().Contains(term)
            || r.CustomerName.ToLowerInvariant().Contains(term)
            || r.LotIds.Any(l => l.ToLowerInvariant().Contains(term));
        if (!matchSearch) return false;

        bool Contains(string cellText, string filterBox) =>
            string.IsNullOrWhiteSpace(filterBox) ||
            (cellText ?? "").ToLowerInvariant().Contains(filterBox.Trim().ToLowerInvariant());

        var matchDate = FDateFilter.SelectedDate == null || r.Issue.Date.Date == FDateFilter.SelectedDate.Value.Date;
        var matchLotId = string.IsNullOrWhiteSpace(FLotIdFilter.Text) ||
            r.LotIds.Any(l => l.ToLowerInvariant().Contains(FLotIdFilter.Text.Trim().ToLowerInvariant()));

        return matchDate && matchLotId &&
            Contains(r.Id, FIdFilter.Text) &&
            Contains(r.QtyText, FQtyFilter.Text) &&
            Contains(r.VolText, FVolFilter.Text) &&
            Contains(r.ValText, FValFilter.Text);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_issView == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        BtnClearColumnFilters.Visibility = AnyColumnFilterActive() ? Visibility.Visible : Visibility.Collapsed;
        _issView.Refresh();
        UpdateEmpty();
    }

    private void UpdateEmpty()
    {
        EmptyRow.Visibility = _issView.Cast<object>().Any() ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------- Bộ lọc theo từng cột ----------------

    private bool AnyColumnFilterActive() =>
        !string.IsNullOrWhiteSpace(FIdFilter.Text) || !string.IsNullOrWhiteSpace(FLotIdFilter.Text) ||
        !string.IsNullOrWhiteSpace(FQtyFilter.Text) || !string.IsNullOrWhiteSpace(FVolFilter.Text) ||
        !string.IsNullOrWhiteSpace(FValFilter.Text) || FDateFilter.SelectedDate != null ||
        ((FilterOrder.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL") != "ALL";

    private void BtnToggleColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        var expand = ColumnFilterPanel.Visibility != Visibility.Visible;
        ColumnFilterPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        ToggleColumnFiltersLabel.Text = expand ? "Ẩn lọc theo cột" : "Lọc theo cột";
    }

    private void BtnClearColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in new[] { FIdFilter, FLotIdFilter, FQtyFilter, FVolFilter, FValFilter })
            box.Text = "";
        FDateFilter.SelectedDate = null;
        FilterOrder.SelectedIndex = 0;
        BtnClearColumnFilters.Visibility = Visibility.Collapsed;
        _issView.Refresh();
        UpdateEmpty();
    }
}

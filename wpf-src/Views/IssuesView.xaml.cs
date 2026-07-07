using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TimberFlowDesktop.Data;
using TimberFlowDesktop.Domain;
using TimberFlowDesktop.Helpers;

namespace TimberFlowDesktop.Views;

public partial class IssuesView : UserControl, IModuleView
{
    /// <summary>Một dòng xuất kho đang soạn.</summary>
    private sealed class DraftItem
    {
        public string WoodLotId = "";
        public string Quantity = "10";

        // Kết quả kiểm tra/tính toán gần nhất
        public WoodLot Lot;
        public bool IsValid;
        public string Error;
        public double Cbm;
        public decimal CostPriceVnd;
        public decimal TotalValueVnd;
    }

    private readonly List<DraftItem> _draftItems = new();

    public IssuesView()
    {
        InitializeComponent();
        FDate.Text = Fmt.Date(DateTime.Today);
        ResetDraft();
        RefreshView();
        Helpers.GridLayoutStore.Attach(HistoryGrid, "issues");
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

    private static double D(string s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>Tính toán/kiểm tra một dòng xuất — logic khớp bản web.</summary>
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
            return;
        }
        item.CostPriceVnd = item.Lot.CostPriceVnd;

        var qty = (int)D(item.Quantity);
        if (qty <= 0)
        {
            item.IsValid = false;
            item.Error = "Số lượng phải lớn hơn 0";
            return;
        }
        if (qty > item.Lot.Quantity)
        {
            item.IsValid = false;
            item.Error = $"Số lượng vượt quá tồn kho khả dụng ({item.Lot.Quantity})";
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
        if (cbm > item.Lot.RemainingCbm) cbm = item.Lot.RemainingCbm;

        item.Cbm = cbm;
        item.TotalValueVnd = Math.Round(item.Lot.CostPriceVnd * (decimal)cbm, 0);
        item.IsValid = true;
        item.Error = null;
    }

    // ---------------- Bảng soạn phiếu ----------------

    private void RebuildRows()
    {
        IssueRowsPanel.Items.Clear();
        foreach (var item in _draftItems)
            IssueRowsPanel.Items.Add(BuildRow(item));
        UpdateTotalsAndErrors();
    }

    private FrameworkElement BuildRow(DraftItem item)
    {
        Recalculate(item);
        var availableLots = AppState.Lots.Where(l => l.Quantity > 0).ToList();

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        foreach (var w in new[] { 220.0, -1, 150, 100, 115, 120, 120, 50 })
            grid.ColumnDefinitions.Add(w < 0
                ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 }
                : new ColumnDefinition { Width = new GridLength(w) });

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
                ? $"{item.Lot.Quantity} thanh / {Fmt.M3Short(item.Lot.RemainingCbm)} m³" : "-";
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
        Grid.SetColumn(lotCombo, 0); grid.Children.Add(lotCombo);

        Grid.SetColumn(infoPanel, 1); grid.Children.Add(infoPanel);
        Grid.SetColumn(availText, 2); grid.Children.Add(availText);

        // Số lượng xuất
        var qtyBox = new TextBox
        {
            Style = (Style)FindResource("CellInputMono"),
            Text = item.Quantity, TextAlignment = TextAlignment.Center,
            Width = 80, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        qtyBox.TextChanged += (_, _) => { item.Quantity = qtyBox.Text; UpdateCells(); };
        Grid.SetColumn(qtyBox, 3); grid.Children.Add(qtyBox);

        Grid.SetColumn(cbmText, 4); grid.Children.Add(cbmText);
        Grid.SetColumn(costText, 5); grid.Children.Add(costText);
        Grid.SetColumn(totalText, 6); grid.Children.Add(totalText);

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
        Grid.SetColumn(del, 7); grid.Children.Add(del);

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

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
        => AddFormPanel.Visibility = AddFormPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
        => AddFormPanel.Visibility = Visibility.Collapsed;

    private void BtnSaveIssue_Click(object sender, RoutedEventArgs e)
    {
        var orderId = (FOrder.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        if (orderId.Length == 0)
        {
            MessageBox.Show("Vui lòng chọn hoặc lập Đơn hàng sản xuất.", "TimberFlow ERP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var item in _draftItems) Recalculate(item);
        var invalid = _draftItems.FirstOrDefault(i => !i.IsValid);
        if (invalid != null)
        {
            MessageBox.Show($"Lỗi danh sách xuất kho: {invalid.Error}. Vui lòng kiểm tra lại.",
                "TimberFlow ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!DateTime.TryParseExact(FDate.Text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            date = DateTime.Today;

        var issue = new WarehouseIssue
        {
            Id = $"ISS-{Random.Shared.Next(10000, 99999)}",
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
            AppState.AddIssue(issue);
            AddFormPanel.Visibility = Visibility.Collapsed;
            ResetDraft();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (term.Length == 0) return true;
        return r.Id.ToLowerInvariant().Contains(term)
            || r.OrderId.ToLowerInvariant().Contains(term)
            || r.CustomerName.ToLowerInvariant().Contains(term)
            || r.LotIds.Any(l => l.ToLowerInvariant().Contains(term));
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_issView == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _issView.Refresh();
        UpdateEmpty();
    }

    private void UpdateEmpty()
    {
        EmptyRow.Visibility = _issView.Cast<object>().Any() ? Visibility.Collapsed : Visibility.Visible;
    }
}

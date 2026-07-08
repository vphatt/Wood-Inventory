using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimberFlowDesktop.Data;
using TimberFlowDesktop.Helpers;

namespace TimberFlowDesktop.Views;

public partial class DashboardView : UserControl, IModuleView
{
    public sealed class BarRow
    {
        public string Name { get; set; }
        public string ValueText { get; set; }
        public double Percent { get; set; }
    }

    public sealed class LowStockRow
    {
        public string Id { get; set; }
        public string Meta { get; set; }
        public string QtyText { get; set; }
        public string CbmText { get; set; }
    }

    public DashboardView()
    {
        InitializeComponent();
        RefreshView();
    }

    public void RefreshView()
    {
        var lots = AppState.Lots;

        var totalVolume = lots.Sum(l => l.RemainingCbm);
        var totalValuation = lots.Sum(l => l.TotalValueVnd);
        var lowStockLots = lots.Where(l => l.Quantity <= 30 && l.Quantity > 0).ToList();

        KpiVolume.Text = Fmt.M3(totalVolume);
        KpiValue.Text = Fmt.Vnd(totalValuation);
        KpiLots.Text = lots.Count.ToString();
        KpiLowStock.Text = lowStockLots.Count.ToString();

        // Tỷ lệ tồn theo chủng loại
        WoodTypeBars.ItemsSource = lots
            .GroupBy(l => l.WoodType)
            .Select(g =>
            {
                var vol = g.Sum(l => l.RemainingCbm);
                var percent = totalVolume > 0 ? vol / totalVolume * 100 : 0;
                return new BarRow
                {
                    Name = g.Key,
                    ValueText = $"{Fmt.M3Short(vol)} m³ ({Fmt.Pct1(percent)}%)",
                    Percent = percent
                };
            })
            .ToList();

        // Giá trị nhập theo NCC
        SupplierBars.ItemsSource = lots
            .GroupBy(l => AppState.FindSupplier(l.SupplierId)?.Name ?? "Unknown")
            .Select(g =>
            {
                var val = g.Sum(l => l.TotalValueVnd);
                return new BarRow
                {
                    Name = g.Key,
                    ValueText = Fmt.Vnd(val),
                    Percent = totalValuation > 0 ? (double)(val / totalValuation) * 100 : 0
                };
            })
            .ToList();

        // Danh sách sắp hết hàng
        LowStockEmpty.Visibility = lowStockLots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LowStockList.ItemsSource = lowStockLots.Select(l => new LowStockRow
        {
            Id = l.Id,
            Meta = $"{l.WoodType}{(string.IsNullOrWhiteSpace(l.WoodSubType) ? "" : " · " + l.WoodSubType)} - {l.Grade} - {Fmt.Num(l.ThicknessMm)}mm",
            QtyText = $"{l.Quantity} thanh",
            CbmText = $"{Fmt.M3Short(l.RemainingCbm)} m³ còn lại"
        }).ToList();
    }

    private MainWindow Main => (MainWindow)Window.GetWindow(this);

    private void BtnGoLots_Click(object sender, RoutedEventArgs e) => Main.OpenModule("lots");
    private void QuickReceipts_Click(object sender, MouseButtonEventArgs e) => Main.OpenModule("receipts");
    private void QuickIssues_Click(object sender, MouseButtonEventArgs e) => Main.OpenModule("issues");
    private void QuickQuotations_Click(object sender, MouseButtonEventArgs e) => Main.OpenModule("quotations");
    private void QuickDotnet_Click(object sender, MouseButtonEventArgs e) => Main.OpenModule("dotnet");
}

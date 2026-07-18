using System.Windows;
using System.Windows.Controls;
using WoodInventory.Data;
using WoodInventory.Domain;
using WoodInventory.Helpers;

namespace WoodInventory.Views;

/// <summary>Trang chỉ đọc: lịch sử thay đổi giá của một dòng báo giá (header thông tin dòng + bảng các lần đổi giá).</summary>
public partial class PriceHistoryView : UserControl
{
    /// <summary>Một dòng trong bảng lịch sử.</summary>
    public sealed class HistoryRow
    {
        public QuotationPriceHistory H { get; }
        public string TimeText => H.ChangedAt.ToString("yyyy/MM/dd HH:mm:ss");
        public string PriceText => Fmt.Money(H.Price, H.PriceCurrency);
        public string ReasonText => string.IsNullOrWhiteSpace(H.Reason)
            ? Lang.T("Quotations.PriceHistory.InitialPrice")   // mốc giá khởi tạo (không phải điều chỉnh)
            : H.Reason;
        public HistoryRow(QuotationPriceHistory h) => H = h;
    }

    private readonly Action _back;

    public PriceHistoryView(QuotationItem item, Action back)
    {
        InitializeComponent();
        _back = back;

        var footage = AppState.GetVolumeRule(item.WoodType) == VolumeRule.ByFootage;
        var woodDisplay = string.IsNullOrWhiteSpace(item.WoodSubType) ? item.WoodType : $"{item.WoodType} · {item.WoodSubType}";
        var thickText = footage
            ? Fmt.RangeNote(item.ThicknessMinNote, item.ThicknessMaxNote, item.ThicknessMin, item.ThicknessMax, item.ThicknessOpen)
            : Fmt.RangeOrList(item.ThicknessValues, item.ThicknessMin, item.ThicknessMax, item.ThicknessOpen);
        var widthText = Fmt.RangeOrList(item.WidthValues, item.WidthMin, item.WidthMax, item.WidthOpen);
        var lengthText = Fmt.RangeOrList(item.LengthValues, item.LengthMin, item.LengthMax, item.LengthOpen);

        Subtitle.Text = woodDisplay;

        // Thẻ thông tin dòng (giống viewmode báo giá)
        AddInfo("Quotations.Field.WoodType", woodDisplay);
        AddInfo("Receipts.Column.Origin", Dash(item.Origin));
        AddInfo("Quotations.Field.Grade", Dash(item.Grade));
        AddInfo("Quotations.PriceHistory.CurrentPrice", Fmt.Money(item.Price, item.PriceCurrency));
        AddInfo("Quotations.Label.Thickness", thickText);
        AddInfo("Quotations.Label.Width", widthText);
        AddInfo("Quotations.Label.Length", lengthText);
        AddInfo("Quotations.Field.Spec", Dash(item.Specification));

        // Bảng lịch sử: mới nhất → cũ nhất (theo thời điểm điều chỉnh giảm dần)
        var rows = AppState.GetPriceHistory(item.Id)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new HistoryRow(h)).ToList();
        HistoryGrid.ItemsSource = rows;
        TotalCount.Text = rows.Count.ToString();
    }

    private static string Dash(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    private void AddInfo(string labelKey, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 16, 14) };
        panel.Children.Add(new TextBlock
        {
            Text = Lang.T(labelKey),
            Style = (Style)FindResource("FieldLabel")
        });
        panel.Children.Add(new TextBox
        {
            Text = value,
            Style = (Style)FindResource("CopyableText")
        });
        InfoPanel.Children.Add(panel);
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) => _back?.Invoke();
}

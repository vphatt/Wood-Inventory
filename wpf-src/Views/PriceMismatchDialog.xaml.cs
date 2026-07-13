using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WoodInventory.Helpers;

namespace WoodInventory.Views;

/// <summary>1 kiện khai đơn giá lệch so với báo giá NCC.</summary>
public record PriceMismatchLine(string LotId, string Quoted, string Entered);

/// <summary>
/// Dialog cảnh báo khi lưu phiếu nhập có kiện gỗ khai đơn giá LỆCH so với báo giá NCC.
/// Không dùng MessageBox được vì cần thêm checkbox ở footer.
/// </summary>
public partial class PriceMismatchDialog : Window
{
    /// <summary>
    /// True = ghi đè đơn giá vừa nhập vào đúng dòng báo giá đã khớp (kèm cập nhật "Chỉnh sửa lần cuối");
    /// False = chỉ lưu đơn giá cho lần nhập này, các lần sau vẫn lấy giá trên báo giá.
    /// </summary>
    public bool UpdateQuotation => ChkUpdateQuotation.IsChecked == true;

    public PriceMismatchDialog(IEnumerable<PriceMismatchLine> lines)
    {
        InitializeComponent();

        // Lấy nguyên template "Kiện {0}: Báo giá {1} — Giá đang nhập {2}" rồi tự ghép Inline để tô màu
        // riêng 2 đơn giá — không dùng string.Format (mất chỗ để phân biệt phần nào là giá).
        var template = Lang.T("Receipts.PriceMismatch.Line");
        var mono = (FontFamily)FindResource("FontMono");
        var body = (Brush)FindResource("Slate700");
        var lotBrush = (Brush)FindResource("Slate800");
        var quotedBrush = (Brush)FindResource("Emerald600");
        var enteredBrush = (Brush)FindResource("Amber600");

        foreach (var line in lines)
        {
            var tb = new TextBlock
            {
                FontFamily = mono,
                FontSize = 12,
                Foreground = body,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5)
            };

            foreach (var part in Regex.Split(template, @"(\{[0-2]\})"))
            {
                if (part.Length == 0) continue;
                tb.Inlines.Add(part switch
                {
                    "{0}" => new Run(line.LotId) { FontWeight = FontWeights.SemiBold, Foreground = lotBrush },
                    "{1}" => new Run(line.Quoted) { FontWeight = FontWeights.SemiBold, Foreground = quotedBrush },
                    "{2}" => new Run(line.Entered) { FontWeight = FontWeights.SemiBold, Foreground = enteredBrush },
                    _ => new Run(part)
                });
            }

            MismatchList.Children.Add(tb);
        }
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

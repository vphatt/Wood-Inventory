using System.Globalization;

namespace WoodInventory.Helpers;

/// <summary>Định dạng số liệu hiển thị — thống nhất kiểu vi-VN: dấu chấm phân cách hàng nghìn, dấu phẩy thập phân.</summary>
public static class Fmt
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>"392.286.085 ₫"</summary>
    public static string Vnd(decimal value) => string.Format(Vi, "{0:C0}", value);
    public static string Vnd(double value) => Vnd((decimal)Math.Round(value));

    /// <summary>"29,6798" (4 số lẻ, dấu phẩy thập phân)</summary>
    public static string M3(double value) => value.ToString("N4", Vi);

    /// <summary>"21,433" (3 số lẻ)</summary>
    public static string M3Short(double value) => value.ToString("N3", Vi);

    /// <summary>"$1.150"</summary>
    public static string Usd(decimal value) => "$" + value.ToString("N0", Vi);

    /// <summary>"25.450"</summary>
    public static string N0(decimal value) => value.ToString("N0", Vi);
    public static string N0(double value) => value.ToString("N0", Vi);

    /// <summary>"2026-05-20" — khớp ISO của bản web</summary>
    public static string Date(DateTime value) => value.ToString("yyyy-MM-dd");

    /// <summary>Ghép "26 x 150 x 2.400" gọn — vẫn có phân cách hàng nghìn khi số lớn.</summary>
    public static string Num(double value) => value.ToString("#,##0.##", Vi);

    /// <summary>"23,5" (1 số lẻ, dấu phẩy thập phân) — dùng cho tỷ lệ %.</summary>
    public static string Pct1(double value) => value.ToString("N1", Vi);

    /// <summary>
    /// Parse ngược chuỗi số đã format kiểu vi-VN (vd "2.400" hoặc "25,4") về double.
    /// Dùng cho MỌI ô nhập số trong app để khớp với Num()/N0()/M3() — tránh lẫn dấu chấm hàng nghìn
    /// thành dấu thập phân. Trả 0 nếu không parse được.
    /// </summary>
    public static double ParseNum(string s) =>
        double.TryParse(s, NumberStyles.Any, Vi, out var v) ? v : 0;

    /// <summary>"20–30mm" / "≥150mm" / "≤2400mm" / "25mm" (min=max) / "Bất kỳ" (cả hai null).</summary>
    public static string Range(double? min, double? max, string unit = "mm")
    {
        if (min == null && max == null) return "Bất kỳ";
        if (min != null && max != null)
            return min == max ? $"{Num(min.Value)}{unit}" : $"{Num(min.Value)}–{Num(max.Value)}{unit}";
        return min != null ? $"≥{Num(min.Value)}{unit}" : $"≤{Num(max.Value)}{unit}";
    }

    /// <summary>
    /// Như <see cref="Range"/> nhưng hiển thị theo ký hiệu gốc (vd "4/4\"", "8/4\"") thay vì số mm —
    /// dùng cho độ dày gỗ nhóm Footage. Rơi về <see cref="Range"/> (số mm) nếu không có ký hiệu.
    /// </summary>
    public static string RangeNote(string minNote, string maxNote, double? min, double? max)
    {
        if (string.IsNullOrWhiteSpace(minNote) && string.IsNullOrWhiteSpace(maxNote))
            return Range(min, max);
        if (!string.IsNullOrWhiteSpace(minNote) && !string.IsNullOrWhiteSpace(maxNote))
            return minNote == maxNote ? minNote : $"{minNote}–{maxNote}";
        return !string.IsNullOrWhiteSpace(minNote) ? $"≥{minNote}" : $"≤{maxNote}";
    }
}

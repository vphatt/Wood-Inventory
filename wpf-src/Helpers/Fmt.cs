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

    /// <summary>M³ với số chữ số thập phân tùy chỉnh (mỗi kiện gỗ có thể làm tròn khác nhau).</summary>
    public static string M3(double value, int decimals) => value.ToString("N" + Math.Max(0, decimals), Vi);

    /// <summary>"21,433" (3 số lẻ)</summary>
    public static string M3Short(double value) => value.ToString("N3", Vi);

    /// <summary>"$1.150"</summary>
    public static string Usd(decimal value) => "$" + value.ToString("N0", Vi);

    /// <summary>Tiền theo đơn vị báo giá — "VND" dùng format ₫ (Vnd), còn lại (mặc định USD) dùng $ (Usd).</summary>
    public static string Money(decimal value, string currency) =>
        string.Equals(currency, "VND", StringComparison.OrdinalIgnoreCase) ? Vnd(value) : Usd(value);

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

    /// <summary>"20–30mm" / "≥150mm" / "≤2400mm" / "25mm" (min=max) / "-" (cả hai null, để trống = wildcard).</summary>
    public static string Range(double? min, double? max, string unit = "mm")
    {
        if (min == null && max == null) return "-";
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

    /// <summary>
    /// Tách chuỗi danh sách giá trị RỜI RẠC tương đương "1220/2440/3000" thành các số (vi-VN aware,
    /// chấp nhận "1.220" hay "1220" đều ra 1220). Bỏ qua phần tử rỗng/không parse được. Dùng cho báo giá
    /// (QuotationItem.ThicknessValues/WidthValues/LengthValues) và khi nhập kho tra khớp/gợi ý theo từng giá trị.
    /// </summary>
    public static List<double> ParseValueList(string raw)
    {
        var result = new List<double>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var part in raw.Split('/'))
        {
            var t = part.Trim();
            if (t.Length == 0) continue;
            if (double.TryParse(t, NumberStyles.Any, Vi, out var v)) result.Add(v);
        }
        return result;
    }

    /// <summary>Như <see cref="Range"/> nhưng ưu tiên hiện danh sách giá trị rời rạc (vd "1.220/2.440/3.000mm")
    /// nếu <paramref name="valuesRaw"/> có dữ liệu; rơi về Range (min/max) nếu không.</summary>
    public static string RangeOrList(string valuesRaw, double? min, double? max, string unit = "mm")
    {
        var list = ParseValueList(valuesRaw);
        return list.Count > 0 ? string.Join("/", list.Select(v => Num(v))) + unit : Range(min, max, unit);
    }
}

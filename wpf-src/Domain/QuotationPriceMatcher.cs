using WoodInventory.Helpers;

namespace WoodInventory.Domain;

/// <summary>
/// Khớp giá cho một kiện gỗ cụ thể theo danh sách báo giá của NCC.
/// Field NULL trên dòng giá luôn khớp; field đã SET thì giá trị thực tế phải nằm
/// trong khoảng (hoặc bằng, với text) mới tính là khớp. Trong các dòng khớp, chọn
/// dòng cụ thể nhất (nhiều field SET nhất) — kiểu specificity như CSS/firewall rule.
/// </summary>
public static class QuotationPriceMatcher
{
    public static QuotationItem FindBestMatch(
        IEnumerable<QuotationItem> items, string woodType,
        double? thickness = null, double? width = null, double? length = null,
        string grade = null, string origin = null, string woodSubType = null)
    {
        QuotationItem best = null;
        var bestSpecificity = -1;

        foreach (var it in items)
        {
            if (!string.Equals(it.WoodType?.Trim(), woodType?.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            // Phân loại con: dòng giá để trống = áp cho mọi con (fallback cấp cha);
            // đã set thì phải khớp đúng con → dòng khớp con luôn cụ thể hơn (Specificity +1).
            if (!TextMatches(it.WoodSubType, woodSubType)) continue;

            if (!ValueMatches(it.ThicknessValues, it.ThicknessMin, it.ThicknessMax, it.ThicknessOpen, thickness)) continue;
            if (!ValueMatches(it.WidthValues, it.WidthMin, it.WidthMax, it.WidthOpen, width)) continue;
            if (!ValueMatches(it.LengthValues, it.LengthMin, it.LengthMax, it.LengthOpen, length)) continue;
            if (!TextMatches(it.Grade, grade)) continue;
            if (!TextMatches(it.Origin, origin)) continue;

            var specificity = Specificity(it);
            if (specificity > bestSpecificity)
            {
                best = it;
                bestSpecificity = specificity;
            }
        }

        return best;
    }

    /// <summary>
    /// Khớp theo khoảng Min/Max. <paramref name="open"/> = false → ĐOẠN đóng [a;b] (a ≤ x ≤ b); true → KHOẢNG
    /// mở (a;b) (a &lt; x &lt; b). Cờ mở áp cho CẢ 2 bound đang set (chỉ set 1 bound thì thành ≥/&gt; hoặc ≤/&lt;).
    /// </summary>
    private static bool RangeMatches(double? min, double? max, bool open, double? actual)
    {
        if (min == null && max == null) return true;       // không giới hạn
        if (actual == null) return false;                  // dòng giá yêu cầu nhưng không có giá trị thực tế
        if (min != null)
        {
            // Cận dưới MỞ (loại đúng min) khi: khoảng mở, HOẶC min = 0 (kích thước gỗ = 0 vô nghĩa → luôn > 0).
            var lowerOpen = open || Math.Abs(min.Value) < 1e-9;
            if (lowerOpen ? actual <= min : actual < min) return false;
        }
        if (max != null && (open ? actual >= max : actual > max)) return false;
        return true;
    }

    /// <summary>
    /// Danh sách giá trị RỜI RẠC tương đương (vd "1220/2440/3000") có ưu tiên cao hơn Min/Max — khi đã set thì
    /// giá trị thực tế phải khớp CHÍNH XÁC (dung sai 0,01mm) 1 trong các giá trị đó, KHÔNG phải nằm trong khoảng
    /// liên tục. Không set thì rơi về <see cref="RangeMatches"/> (đóng/mở theo <paramref name="open"/>).
    /// </summary>
    /// <summary>Public wrapper của khớp kích thước 1 chiều — dùng để LỌC gợi ý cascade ở Nhập Kho
    /// (chọn Dày 23 → chỉ item có Dày khớp 23 mới đem gợi ý Rộng/Dài).</summary>
    public static bool DimensionMatches(string valuesRaw, double? min, double? max, bool open, double? actual) =>
        ValueMatches(valuesRaw, min, max, open, actual);

    private static bool ValueMatches(string valuesRaw, double? min, double? max, bool open, double? actual)
    {
        var list = Fmt.ParseValueList(valuesRaw);
        if (list.Count == 0) return RangeMatches(min, max, open, actual);
        if (actual == null) return false;
        return list.Any(v => Math.Abs(v - actual.Value) < 0.01);
    }

    private static bool TextMatches(string ruleValue, string actual)
    {
        if (string.IsNullOrWhiteSpace(ruleValue)) return true;
        return string.Equals(ruleValue.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int Specificity(QuotationItem it)
    {
        var n = 0;
        if (!string.IsNullOrWhiteSpace(it.WoodSubType)) n++;
        if (it.ThicknessMin != null || it.ThicknessMax != null || !string.IsNullOrWhiteSpace(it.ThicknessValues)) n++;
        if (it.WidthMin != null || it.WidthMax != null || !string.IsNullOrWhiteSpace(it.WidthValues)) n++;
        if (it.LengthMin != null || it.LengthMax != null || !string.IsNullOrWhiteSpace(it.LengthValues)) n++;
        if (!string.IsNullOrWhiteSpace(it.Grade)) n++;
        if (!string.IsNullOrWhiteSpace(it.Origin)) n++;
        return n;
    }
}

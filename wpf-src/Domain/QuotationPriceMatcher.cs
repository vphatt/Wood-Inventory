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

            if (!RangeMatches(it.ThicknessMin, it.ThicknessMax, thickness)) continue;
            if (!RangeMatches(it.WidthMin, it.WidthMax, width)) continue;
            if (!RangeMatches(it.LengthMin, it.LengthMax, length)) continue;
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

    private static bool RangeMatches(double? min, double? max, double? actual)
    {
        if (min == null && max == null) return true;       // không giới hạn
        if (actual == null) return false;                  // dòng giá yêu cầu nhưng không có giá trị thực tế
        if (min != null && actual < min) return false;
        if (max != null && actual > max) return false;
        return true;
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
        if (it.ThicknessMin != null || it.ThicknessMax != null) n++;
        if (it.WidthMin != null || it.WidthMax != null) n++;
        if (it.LengthMin != null || it.LengthMax != null) n++;
        if (!string.IsNullOrWhiteSpace(it.Grade)) n++;
        if (!string.IsNullOrWhiteSpace(it.Origin)) n++;
        return n;
    }
}

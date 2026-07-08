using TimberFlowDesktop.Helpers;

namespace TimberFlowDesktop.Domain;

/// <summary>
/// Dịch vụ nghiệp vụ trung tâm: tính thể tích gỗ (m³), giá vốn và giá trị tồn kho.
/// Mọi màn hình đều dùng chung để đảm bảo công thức thống nhất.
/// </summary>
public static class WoodVolumeCalculator
{
    /// <summary>
    /// Parse độ dày cho gỗ nhóm Footage — chấp nhận thêm ký hiệu ngành gỗ Mỹ vì số mm chính xác
    /// không có ý nghĩa tính toán với nhóm này (công thức chỉ dùng Footage):
    ///  - "1\"" (a") = 1 inch = 25.4mm
    ///  - "4/4\"" (a/b", hệ quarter) = (a/b) inch, vd 4/4"=25.4mm, 5/4"=31.75mm
    ///  - Số thường không có " hay / vẫn hiểu là mm như cũ (tương thích ngược).
    /// </summary>
    public static double ParseFootageThicknessMm(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return 0;

        var isInches = text.EndsWith("\"");
        if (isInches) text = text[..^1].Trim();

        var slash = text.IndexOf('/');
        if (slash > 0)
        {
            var a = Fmt.ParseNum(text[..slash].Trim());
            var b = Fmt.ParseNum(text[(slash + 1)..].Trim());
            return b != 0 ? Math.Round(a / b * 25.4, 4) : 0;
        }

        var value = Fmt.ParseNum(text);
        return isInches ? Math.Round(value * 25.4, 4) : value;
    }

    /// <summary>
    /// Tính thể tích m³ theo chủng loại gỗ.
    ///  - Gỗ Dương (Poplar):  m³ = (Footage / 1000) * 2.36
    ///  - Loại khác:          m³ = Dài * Rộng * Dày * Số lượng / 1.000.000.000
    /// </summary>
    public static double CalculateVolume(string woodType, double thicknessMm, double widthMm,
        double lengthMm, int quantity, double footage)
    {
        var normalized = (woodType ?? "").Trim().ToLowerInvariant();
        var isPoplar = normalized.Contains("dương") || normalized.Contains("duong") || normalized.Contains("poplar");

        if (isPoplar)
            return Math.Round((footage / 1000.0) * 2.36, 4);

        return Math.Round((lengthMm * widthMm * thicknessMm * quantity) / 1_000_000_000.0, 4);
    }

    /// <summary>Giá vốn VND/m³ = Giá USD * Tỷ giá * (1 + Thuế%/100), làm tròn đến đồng.</summary>
    public static decimal CalculateCostPricePerM3(decimal priceUsd, decimal exchangeRate, decimal taxPercent)
    {
        var raw = priceUsd * exchangeRate;
        return Math.Round(raw + raw * (taxPercent / 100m), 0);
    }

    /// <summary>Tổng giá trị VND = Giá vốn/m³ * m³.</summary>
    public static decimal CalculateTotalValue(decimal costPriceVnd, double cbm)
        => Math.Round(costPriceVnd * (decimal)cbm, 0);

    /// <summary>Tổng tiền USD = Đơn giá USD/m³ * Thể tích m³.</summary>
    public static decimal CalculateTotalUsd(decimal priceUsd, double cbm)
        => Math.Round(priceUsd * (decimal)cbm, 2);

    /// <summary>Quy đổi một khoản USD sang VND theo tỷ giá, làm tròn đến đồng.</summary>
    public static decimal ConvertUsdToVnd(decimal amountUsd, decimal exchangeRate)
        => Math.Round(amountUsd * exchangeRate, 0);

    /// <summary>Tiền thuế VND = Tổng tiền VND * Thuế% / 100, làm tròn đến đồng.</summary>
    public static decimal CalculateTaxAmountVnd(decimal totalVnd, decimal taxPercent)
        => Math.Round(totalVnd * taxPercent / 100m, 0);
}

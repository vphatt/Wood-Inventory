namespace TimberFlowDesktop.Domain;

/// <summary>
/// Dịch vụ nghiệp vụ trung tâm: tính thể tích gỗ (m³), giá vốn và giá trị tồn kho.
/// Mọi màn hình đều dùng chung để đảm bảo công thức thống nhất.
/// </summary>
public static class WoodVolumeCalculator
{
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
}

using System.Windows.Controls;

namespace WoodInventory.Views;

public partial class DotNetView : UserControl, IModuleView
{
    public DotNetView()
    {
        InitializeComponent();
        CodeBlock.Text = """
using System;

namespace WoodInventory.Domain;

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
    public static double CalculateVolume(string woodType, double thicknessMm,
        double widthMm, double lengthMm, int quantity, double footage)
    {
        var normalized = (woodType ?? "").Trim().ToLowerInvariant();
        var isPoplar = normalized.Contains("dương")
                    || normalized.Contains("duong")
                    || normalized.Contains("poplar");

        if (isPoplar)
            return Math.Round((footage / 1000.0) * 2.36, 4);

        return Math.Round(
            (lengthMm * widthMm * thicknessMm * quantity) / 1_000_000_000.0, 4);
    }

    /// <summary>Giá vốn VND/m³ = Giá USD * Tỷ giá * (1 + Thuế%/100).</summary>
    public static decimal CalculateCostPricePerM3(
        decimal priceUsd, decimal exchangeRate, decimal taxPercent)
    {
        var raw = priceUsd * exchangeRate;
        return Math.Round(raw + raw * (taxPercent / 100m), 0);
    }

    /// <summary>Tổng giá trị VND = Giá vốn/m³ * m³.</summary>
    public static decimal CalculateTotalValue(decimal costPriceVnd, double cbm)
        => Math.Round(costPriceVnd * (decimal)cbm, 0);
}

// Kiến trúc ứng dụng desktop này:
//   Domain/           — Entities + WoodVolumeCalculator (nghiệp vụ thuần)
//   Data/             — EF Core DbContext (SQLite) + DbSeeder + AppState
//   Views/            — các màn hình WPF chính (Dashboard, Phân Loại Gỗ, Nhà Cung Cấp, Lots, Quotations, Receipts, Issues)
//   MainWindow        — Sidebar + dải tab động + breadcrumb + status bar
//
// Dữ liệu lưu tại: %APPDATA%\WoodInventory\woodinventory.db (SQLite cục bộ)
// Ứng dụng chạy offline 100%%, không phụ thuộc bất kỳ thành phần web nào.
""";
    }

    public void RefreshView() { }
}

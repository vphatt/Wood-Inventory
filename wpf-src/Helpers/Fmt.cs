using System.Globalization;

namespace TimberFlowDesktop.Helpers;

/// <summary>Định dạng số liệu hiển thị — khớp với bản web (Intl vi-VN).</summary>
public static class Fmt
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    /// <summary>"392.286.085 ₫"</summary>
    public static string Vnd(decimal value) => string.Format(Vi, "{0:C0}", value);
    public static string Vnd(double value) => Vnd((decimal)Math.Round(value));

    /// <summary>"29.6798" (4 số lẻ, dấu chấm thập phân)</summary>
    public static string M3(double value) => value.ToString("F4", En);

    /// <summary>"21.433" (3 số lẻ)</summary>
    public static string M3Short(double value) => value.ToString("F3", En);

    /// <summary>"$1,150"</summary>
    public static string Usd(decimal value) => "$" + value.ToString("N0", En);

    /// <summary>"25,450"</summary>
    public static string N0(decimal value) => value.ToString("N0", En);
    public static string N0(double value) => value.ToString("N0", En);

    /// <summary>"2026-05-20" — khớp ISO của bản web</summary>
    public static string Date(DateTime value) => value.ToString("yyyy-MM-dd");

    /// <summary>Ghép "26 x 150 x 2400" gọn.</summary>
    public static string Num(double value) => value.ToString("0.##", En);
}

using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace WoodInventory.Helpers;

/// <summary>
/// Nạp chuỗi dịch từ file JSON (Resources/Lang/*.json) theo ngôn ngữ đang chọn.
/// Singleton implement INotifyPropertyChanged để XAML binding qua indexer (xem <see cref="LocExtension"/>)
/// tự cập nhật ngay khi đổi ngôn ngữ (hot-swap) — không cần khởi động lại app.
/// Phần UI dựng bằng code-behind (đa số View trong app này) không bind được kiểu đó, nên hot-swap ở đó
/// dựa vào việc gọi lại RefreshView()/Build...() sẵn có của từng View khi <see cref="LanguageChanged"/> bắn ra
/// (giống hệt cơ chế AppState.Changed đang dùng).
/// </summary>
public sealed class LanguageService : INotifyPropertyChanged
{
    public static LanguageService Instance { get; } = new();

    /// <summary>Danh sách ngôn ngữ hỗ trợ, hiện theo đúng thứ tự này ở ComboBox chọn ngôn ngữ.</summary>
    public static readonly (string Code, string Label)[] Available =
    {
        ("vi", "Tiếng Việt"),
        ("zh-Hans", "中文（简体）"),
    };

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>Bắn ra SAU khi đã đổi xong dữ liệu chuỗi — View nghe sự kiện này để tự rebuild phần code-behind.</summary>
    public event Action LanguageChanged;

    private Dictionary<string, string> _strings = new();
    private Dictionary<string, string> _fallback = new();

    public string CurrentLanguage { get; private set; } = "vi";

    private LanguageService() { }

    /// <summary>Gọi 1 lần lúc khởi động app, trước khi dựng bất kỳ View nào.</summary>
    public void Initialize(string language)
    {
        _fallback = LoadFile("vi");
        SetLanguage(string.IsNullOrWhiteSpace(language) ? "vi" : language, notify: false);
    }

    /// <summary>Đổi ngôn ngữ đang hiển thị + bắn sự kiện cho toàn app tự cập nhật.</summary>
    public void SetLanguage(string language, bool notify = true)
    {
        CurrentLanguage = string.IsNullOrWhiteSpace(language) ? "vi" : language;
        _strings = CurrentLanguage == "vi" ? _fallback : LoadFile(CurrentLanguage);
        if (!notify) return;

        // "Item[]" là quy ước của WPF binding engine: báo TOÀN BỘ giá trị indexer đã đổi,
        // buộc mọi Binding "{helpers:Loc Key}" đang hiển thị phải đọc lại giá trị mới.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    private static Dictionary<string, string> LoadFile(string language)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Lang", $"{language}.json");
        if (!File.Exists(path)) return new Dictionary<string, string>();
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Cho phép XAML binding kiểu Path="[Key]" (xem LocExtension).</summary>
    public string this[string key] => T(key);

    /// <summary>Tra chuỗi theo key — thiếu ở ngôn ngữ đang chọn thì rơi về tiếng Việt, thiếu luôn thì trả về chính key
    /// (để lộ ra chỗ chưa dịch thay vì hiển thị rỗng/crash).</summary>
    public string T(string key, params object[] args)
    {
        var template = _strings.TryGetValue(key, out var v) ? v
            : _fallback.TryGetValue(key, out var fv) ? fv
            : key;
        return args is { Length: > 0 } ? string.Format(template, args) : template;
    }
}

/// <summary>Lối tắt tĩnh cho code-behind, khớp phong cách <c>Fmt</c>/<c>AppState</c> sẵn có trong app.</summary>
public static class Lang
{
    public static string T(string key, params object[] args) => LanguageService.Instance.T(key, args);
    public static void SetLanguage(string language) => LanguageService.Instance.SetLanguage(language);
    public static string CurrentLanguage => LanguageService.Instance.CurrentLanguage;
}

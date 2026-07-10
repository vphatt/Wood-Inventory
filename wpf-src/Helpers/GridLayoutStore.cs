using System.IO;
using System.Text.Json;
using System.Windows.Controls;

namespace WoodInventory.Helpers;

/// <summary>
/// Lưu/khôi phục THỨ TỰ cột DataGrid theo user (không lưu độ rộng — cột đã cố định độ rộng, tắt resize
/// hẳn qua style DataTable/App.xaml). Ghi vào %APPDATA%\WoodInventory\grid-layout.json (per-user), tự đọc
/// lại mỗi lần mở app nên thứ tự cột giữ nguyên dù tắt/mở lại phần mềm.
/// </summary>
public static class GridLayoutStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WoodInventory", "grid-layout.json");

    private static Dictionary<string, List<string>> _cache;

    /// <summary>Gắn lưu/khôi phục thứ tự cột cho 1 DataGrid — gọi 1 lần trong constructor View, sau khi cột đã tạo.</summary>
    public static void Attach(DataGrid grid, string key)
    {
        grid.Loaded += (_, _) => ApplySaved(grid, key);
        grid.ColumnReordered += (_, _) => Save(grid, key);
    }

    /// <summary>Khớp cột đã lưu theo Header (text). Cột ghim (CanUserReorder=False, vd STT) luôn giữ nguyên đầu bảng.</summary>
    private static void ApplySaved(DataGrid grid, string key)
    {
        if (!Load().TryGetValue(key, out var order)) return;

        var pinnedCount = grid.Columns.Count(c => !c.CanUserReorder);
        var reorderable = grid.Columns.Where(c => c.CanUserReorder)
            .ToDictionary(c => c.Header?.ToString() ?? "", c => c);

        var index = pinnedCount;
        foreach (var header in order)
        {
            if (reorderable.Remove(header, out var col))
                col.DisplayIndex = index++;
        }
        // Cột mới thêm sau này (không có trong layout đã lưu) xếp tiếp theo, giữ nguyên thứ tự khai báo.
        foreach (var col in reorderable.Values.OrderBy(c => c.DisplayIndex))
            col.DisplayIndex = index++;
    }

    private static void Save(DataGrid grid, string key)
    {
        var order = grid.Columns
            .Where(c => c.CanUserReorder)
            .OrderBy(c => c.DisplayIndex)
            .Select(c => c.Header?.ToString() ?? "")
            .ToList();

        var all = Load();
        all[key] = order;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
        }
        catch { /* lưu bố cục là tiện ích phụ, lỗi ghi file không được làm crash app */ }
    }

    private static Dictionary<string, List<string>> Load()
    {
        if (_cache != null) return _cache;
        try
        {
            _cache = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(FilePath))
                : new Dictionary<string, List<string>>();
        }
        catch
        {
            _cache = new Dictionary<string, List<string>>();
        }
        return _cache;
    }
}

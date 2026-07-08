using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;

namespace TimberFlowDesktop.Helpers;

/// <summary>
/// Lưu &amp; khôi phục bố cục cột của DataGrid (thứ tự + độ rộng) theo từng bảng.
/// Ghi ra file JSON ở %APPDATA%\TimberFlowDesktop\grid-layout.json (theo user Windows),
/// nên giữ nguyên kể cả khi đóng/mở lại tab hay thoát app.
/// </summary>
public static class GridLayoutStore
{
    private sealed class ColState
    {
        public string Col { get; set; }   // khóa cột (theo Header)
        public int Order { get; set; }     // DisplayIndex
        public double W { get; set; }      // Width.Value
        public int U { get; set; }         // DataGridLengthUnitType
    }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimberFlowDesktop", "grid-layout.json");

    private static readonly Dictionary<string, List<ColState>> All = Load();

    private static Dictionary<string, List<ColState>> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, List<ColState>>>(File.ReadAllText(FilePath))
                       ?? new();
        }
        catch { /* file hỏng → bỏ qua, dùng mặc định */ }
        return new();
    }

    private static void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(All));
        }
        catch { /* không ghi được thì thôi, không làm crash app */ }
    }

    private static string ColKey(DataGridColumn c) =>
        c.Header?.ToString() ?? c.SortMemberPath ?? "";

    /// <summary>Gắn cơ chế lưu/khôi phục vào một DataGrid với khóa định danh riêng.</summary>
    public static void Attach(DataGrid grid, string key)
    {
        var applying = false;

        void ApplySaved()
        {
            if (!All.TryGetValue(key, out var states) || states.Count == 0) return;
            applying = true;
            try
            {
                // Độ rộng
                foreach (var col in grid.Columns)
                {
                    var st = states.FirstOrDefault(s => s.Col == ColKey(col));
                    if (st != null && st.W > 0)
                        col.Width = new DataGridLength(st.W, (DataGridLengthUnitType)st.U);
                }
                // Thứ tự: áp theo Order tăng dần
                foreach (var st in states.OrderBy(s => s.Order))
                {
                    var col = grid.Columns.FirstOrDefault(c => ColKey(c) == st.Col);
                    if (col != null && st.Order >= 0 && st.Order < grid.Columns.Count)
                        col.DisplayIndex = st.Order;
                }
                // Cột STT luôn ghim vị trí đầu — layout đã lưu từ trước khi có cột STT
                // (bảng cũ) không được đẩy nó ra sau.
                var sttCol = grid.Columns.FirstOrDefault(c => ColKey(c) == "STT");
                if (sttCol != null) sttCol.DisplayIndex = 0;
            }
            catch { /* dữ liệu lệch → bỏ qua */ }
            finally { applying = false; }
        }

        void Save()
        {
            if (applying) return;
            All[key] = grid.Columns.Select(c => new ColState
            {
                Col = ColKey(c),
                Order = c.DisplayIndex,
                W = (c.Width.IsAbsolute || c.Width.IsStar) ? c.Width.Value : c.ActualWidth,
                U = (int)c.Width.UnitType
            }).ToList();
            Persist();
        }

        // Bẫy đã dính: lần đầu tiên mở app (chưa có file grid-layout.json → ApplySaved() ở trên
        // không set lại Width cột nào cả) thì kéo tay viền cột để resize KHÔNG ăn — phải double-click
        // vào viền 1 lần (thao tác auto-fit có sẵn của DataGrid, tự set thẳng vào Width) thì các lần
        // kéo sau mới bình thường. Tức là DataGrid cần một lượt set-Width THẬT SỰ (đổi giá trị rồi đổi
        // lại) để "khởi động" đúng phần hình học của gripper resize; các lần chạy sau có file đã lưu nên
        // ApplySaved() ở trên vô tình làm việc này giúp, chỉ lần đầu tiên là thiếu. Giả lập lại thao tác
        // double-click đó ngay sau khi DataGrid Loaded để mọi lần mở đều nhất quán, không phân biệt
        // có file layout hay chưa.
        void PrimeColumnWidths()
        {
            applying = true;
            try
            {
                foreach (var col in grid.Columns)
                {
                    var w = col.Width;
                    col.Width = new DataGridLength(w.Value + 1, w.UnitType);
                    col.Width = w;
                }
            }
            finally { applying = false; }
        }
        grid.Loaded += (_, _) => PrimeColumnWidths();

        ApplySaved();

        // Lưu khi kéo đổi thứ tự cột
        grid.ColumnReordered += (_, _) => Save();

        // Lưu khi kéo giãn độ rộng (thao tác resize của user set thẳng vào Width DP)
        foreach (var col in grid.Columns)
        {
            DependencyPropertyDescriptor
                .FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
                ?.AddValueChanged(col, (_, _) => Save());
        }
    }
}

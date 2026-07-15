using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WoodInventory.Helpers;

/// <summary>
/// Message box kiểu Windows 11 (dùng TaskDialog gốc của OS — bo góc, icon lớn) thay cho MessageBox.Show cổ điển.
/// Cố tình để CÙNG chữ ký với MessageBox.Show (trả về MessageBoxResult) để thay 1-đổi-1 gọn ở mọi nơi:
/// chỉ cần đổi "MessageBox.Show(...)" thành "AppDialog.Show(...)", giữ nguyên tham số.
/// P/Invoke hàm TaskDialog trong comctl32 — cần ComCtl32 v6 (đã khai dependency trong app.manifest). Nếu vì
/// lý do gì TaskDialog lỗi (HRESULT khác S_OK hoặc ném exception) thì tự rơi về MessageBox.Show cổ điển,
/// KHÔNG bao giờ nuốt mất dialog.
/// </summary>
public static class AppDialog
{
    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, EntryPoint = "TaskDialog")]
    private static extern int TaskDialog(
        IntPtr hwndParent, IntPtr hInstance,
        string pszWindowTitle, string pszMainInstruction, string pszContent,
        int dwCommonButtons, IntPtr pszIcon, out int pnButton);

    // TASKDIALOG_COMMON_BUTTON_FLAGS
    private const int TDCBF_OK = 0x0001, TDCBF_YES = 0x0002, TDCBF_NO = 0x0004, TDCBF_CANCEL = 0x0008;
    // Icon chuẩn của TaskDialog = MAKEINTRESOURCE(-n) → (IntPtr)(ushort)(-n).
    private static readonly IntPtr TD_WARNING = (IntPtr)65535;      // -1  (tam giác vàng)
    private static readonly IntPtr TD_ERROR = (IntPtr)65534;        // -2  (vòng đỏ)
    private static readonly IntPtr TD_INFORMATION = (IntPtr)65533;  // -3  (vòng "i" xanh — như ảnh mẫu)
    // Win32 button ID trả về qua pnButton
    private const int IDOK = 1, IDCANCEL = 2, IDYES = 6, IDNO = 7;

    /// <summary>Thay thẳng cho MessageBox.Show — cùng tham số, cùng kiểu trả về.</summary>
    public static MessageBoxResult Show(string text, string title = "",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        try
        {
            // pszMainInstruction = null → chỉ hiện text thường cạnh icon (gọn như ảnh mẫu, không có tiêu đề xanh đậm).
            var hr = TaskDialog(OwnerHandle(), IntPtr.Zero, title ?? "", null, text ?? "",
                CommonButtons(buttons), IconOf(icon), out var pressed);
            if (hr != 0) return Fallback(text, title, buttons, icon);   // S_OK = 0
            return pressed switch
            {
                IDYES => MessageBoxResult.Yes,
                IDNO => MessageBoxResult.No,
                IDCANCEL => MessageBoxResult.Cancel,
                IDOK => MessageBoxResult.OK,
                _ => MessageBoxResult.None,
            };
        }
        catch
        {
            return Fallback(text, title, buttons, icon);
        }
    }

    private static MessageBoxResult Fallback(string text, string title, MessageBoxButton buttons, MessageBoxImage icon) =>
        MessageBox.Show(text ?? "", title ?? "", buttons, icon);

    private static int CommonButtons(MessageBoxButton b) => b switch
    {
        MessageBoxButton.OKCancel => TDCBF_OK | TDCBF_CANCEL,
        MessageBoxButton.YesNo => TDCBF_YES | TDCBF_NO,
        MessageBoxButton.YesNoCancel => TDCBF_YES | TDCBF_NO | TDCBF_CANCEL,
        _ => TDCBF_OK,
    };

    // Error/Hand/Stop = 16 (gộp 1 case), Warning/Exclamation = 48, Information/Asterisk = 64, Question = 32.
    // TaskDialog không có icon dấu "?" → câu hỏi xác nhận (Question) dùng icon cảnh báo cho hợp ngữ cảnh.
    private static IntPtr IconOf(MessageBoxImage icon) => icon switch
    {
        MessageBoxImage.Error => TD_ERROR,
        MessageBoxImage.Warning => TD_WARNING,
        MessageBoxImage.Question => TD_WARNING,
        MessageBoxImage.Information => TD_INFORMATION,
        _ => IntPtr.Zero,
    };

    /// <summary>HWND của cửa sổ đang active (để dialog modal + canh giữa đúng cửa sổ); IntPtr.Zero nếu chưa có.</summary>
    private static IntPtr OwnerHandle()
    {
        var app = Application.Current;
        if (app == null) return IntPtr.Zero;
        var win = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
        return win != null ? new WindowInteropHelper(win).Handle : IntPtr.Zero;
    }
}

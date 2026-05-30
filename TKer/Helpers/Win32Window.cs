using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TKer.Helpers;

/// <summary>
/// Win32 API でデスクトップ上の他アプリのウィンドウを列挙・最小化・
/// Windows 標準スナップ（Win+矢印）で配置するヘルパー。
/// </summary>
public static class Win32Window
{
    private const int GWL_EXSTYLE     = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW  = 0x00040000;

    /// <summary>SW_SHOWNORMAL: 通常表示。</summary>
    public const int SW_SHOWNORMAL = 1;
    /// <summary>SW_MAXIMIZE: 最大化。</summary>
    public const int SW_MAXIMIZE   = 3;
    /// <summary>SW_MINIMIZE: 最小化。</summary>
    public const int SW_MINIMIZE   = 6;
    /// <summary>SW_RESTORE: 最小化・最大化から元のサイズ・位置に復元。</summary>
    public const int SW_RESTORE    = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int DWMWA_CLOAKED        = 14;
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int MAX_PATH             = 260;
    private const int SM_CXSCREEN          = 0;
    private const int SM_CYSCREEN          = 1;

    // 仮想キーコード（Win+矢印スナップ用）
    private const byte VK_LWIN   = 0x5B;
    private const byte VK_MENU   = 0x12; // Alt
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_LEFT   = 0x25;
    private const byte VK_UP     = 0x26;
    private const byte VK_RIGHT  = 0x27;
    private const byte VK_DOWN   = 0x28;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT  rcNormalPosition;
    }

    /// <summary>列挙したウィンドウの識別情報と位置情報を保持するレコード。</summary>
    public record WindowInfo(
        IntPtr Handle,
        string Title,
        string ClassName,
        string ExePath,
        int X, int Y, int Width, int Height,
        int ShowState);

    /// <summary>可視のトップレベルウィンドウを列挙して返す（ツールウィンドウ・タイトル無しは除外）。</summary>
    public static List<WindowInfo> EnumerateVisibleWindows()
    {
        var list = new List<WindowInfo>();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            // タスクバーに出ないツールウィンドウは除外
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0) return true;

            // DWM 上で cloaked（実際には表示されていない）ウィンドウは除外
            // TextInputHost.exe や仮想デスクトップで非表示の UWP 等を弾く
            try
            {
                if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                    && cloaked != 0)
                    return true;
            }
            catch { /* 古い Windows では DwmGetWindowAttribute 未対応 — 無視 */ }

            int len = GetWindowTextLength(hwnd);
            if (len <= 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            var cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            string className = cls.ToString();

            string exe;
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                using var p = Process.GetProcessById((int)pid);
                exe = p.MainModule?.FileName ?? "";
            }
            catch { exe = ""; }
            if (string.IsNullOrEmpty(exe)) return true;

            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hwnd, ref placement)) return true;

            int x = placement.rcNormalPosition.Left;
            int y = placement.rcNormalPosition.Top;
            int w = placement.rcNormalPosition.Right - placement.rcNormalPosition.Left;
            int h = placement.rcNormalPosition.Bottom - placement.rcNormalPosition.Top;

            list.Add(new WindowInfo(hwnd, title, className, exe, x, y, w, h, placement.showCmd));
            return true;
        }, IntPtr.Zero);

        return list;
    }

    /// <summary>指定ハンドルのウィンドウを最小化する。</summary>
    public static void MinimizeWindow(IntPtr hwnd) => ShowWindow(hwnd, SW_MINIMIZE);

    /// <summary>プライマリディスプレイの物理ピクセルサイズを取得する（DPI 補正済み）。</summary>
    public static (int Width, int Height) GetPrimaryScreenPixelSize()
        => (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    /// <summary>現在のデスクトップ壁紙ファイルのフルパスを取得する。存在しない場合は null。</summary>
    public static string? GetDesktopWallpaperPath()
    {
        try
        {
            var sb = new StringBuilder(MAX_PATH);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0) != 0)
            {
                var path = sb.ToString();
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// ウィンドウを前面化し、Windows 標準の Win+矢印スナップで指定ゾーンに配置する。
    /// これにより本物のスナップ状態になり、境界線調整も Windows 標準で効く。
    /// zone: "Maximize" / "LeftHalf" / "RightHalf" / "TopLeft" / "TopRight" / "BottomLeft" / "BottomRight"
    /// 既知ゾーンなら true を返す。
    /// </summary>
    public static bool FocusAndSnap(IntPtr hwnd, string zone)
    {
        if (string.IsNullOrEmpty(zone)) return false;

        // 復元して前面へ（Alt タップで SetForegroundWindow のフォアグラウンドロックを解除）
        ShowWindow(hwnd, SW_RESTORE);
        System.Threading.Thread.Sleep(80);
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
        System.Threading.Thread.Sleep(150);

        switch (zone)
        {
            case "Maximize":
                ShowWindow(hwnd, SW_MAXIMIZE);
                return true;
            case "LeftHalf":
                WinChord(VK_LEFT);  break;
            case "RightHalf":
                WinChord(VK_RIGHT); break;
            case "TopLeft":
                WinChord(VK_LEFT);  System.Threading.Thread.Sleep(200); WinChord(VK_UP);   break;
            case "BottomLeft":
                WinChord(VK_LEFT);  System.Threading.Thread.Sleep(200); WinChord(VK_DOWN); break;
            case "TopRight":
                WinChord(VK_RIGHT); System.Threading.Thread.Sleep(200); WinChord(VK_UP);   break;
            case "BottomRight":
                WinChord(VK_RIGHT); System.Threading.Thread.Sleep(200); WinChord(VK_DOWN); break;
            default:
                return false;
        }

        // 半分スナップ後に出る Snap Assist（残り領域のウィンドウ選択）を Esc で閉じ、
        // 次のウィンドウのスナップを邪魔しないようにする。
        System.Threading.Thread.Sleep(200);
        keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        return true;
    }

    /// <summary>Win + 指定キー のショートカットを1回送出する。</summary>
    private static void WinChord(byte vk)
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(vk,      0, 0, UIntPtr.Zero);
        keybd_event(vk,      0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TKer.Helpers;

/// <summary>Win32 API でデスクトップ上の他アプリのウィンドウを列挙・移動するヘルパー。</summary>
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
    private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

    private const int DWMWA_CLOAKED               = 14;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int SPI_GETDESKWALLPAPER        = 0x0073;
    private const int MAX_PATH                    = 260;

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

    /// <summary>指定ハンドルのウィンドウを位置・サイズ・表示状態を指定して配置する。</summary>
    public static bool ApplyPlacement(IntPtr hwnd, int x, int y, int width, int height, int showState)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement)) return false;

        placement.showCmd = showState switch
        {
            SW_MAXIMIZE => SW_MAXIMIZE,
            SW_MINIMIZE => SW_MINIMIZE,
            _           => SW_SHOWNORMAL
        };

        // DWM の不可視ボーダー（Windows 10/11 のシャドウ用余白）を補正して、
        // 目に見える枠が指定の x/y/width/height に来るようにする
        AdjustForInvisibleBorders(hwnd, ref x, ref y, ref width, ref height);

        placement.rcNormalPosition.Left   = x;
        placement.rcNormalPosition.Top    = y;
        placement.rcNormalPosition.Right  = x + width;
        placement.rcNormalPosition.Bottom = y + height;

        return SetWindowPlacement(hwnd, ref placement);
    }

    /// <summary>
    /// DWM 拡張フレーム境界と WindowRect の差分を取得して、
    /// 「見た目の右端・下端」が指定座標になるよう x/y/width/height を補正する。
    /// 失敗時は何もしない（古い Windows / DWM 未対応プロセス対応）。
    /// </summary>
    private static void AdjustForInvisibleBorders(IntPtr hwnd, ref int x, ref int y, ref int width, ref int height)
    {
        try
        {
            int size = Marshal.SizeOf<RECT>();
            if (DwmGetWindowAttributeRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT extFrame, size) != 0) return;
            if (!GetWindowRect(hwnd, out RECT winRect)) return;

            int leftPad   = extFrame.Left   - winRect.Left;
            int topPad    = extFrame.Top    - winRect.Top;
            int rightPad  = winRect.Right   - extFrame.Right;
            int bottomPad = winRect.Bottom  - extFrame.Bottom;

            x      -= leftPad;
            y      -= topPad;
            width  += leftPad + rightPad;
            height += topPad  + bottomPad;
        }
        catch { /* 補正失敗時は無視 */ }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TKer.Services;

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
    private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                            int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOZORDER   = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

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

    /// <summary>プライマリディスプレイの物理ピクセルサイズを取得する（DPI 補正済み）。</summary>
    public static (int Width, int Height) GetPrimaryScreenPixelSize()
        => (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

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
    /// 指定ハンドルのウィンドウの「見える枠」が x/y/width/height ぴったりに来るよう配置する。
    /// 決め打ちのボーダー値は使わず、配置 → 実際の見える枠を DWM で実測 → 目標との
    /// ズレ分だけ外枠を補正、を最大3回繰り返して収束させる（自己補正方式）。
    /// これにより不可視ボーダー幅・DPI・アプリごとの差に依存せず正確に配置できる。
    /// DWM 実測が取れない環境では素の配置にフォールバックする（過補正で重ねない）。
    /// </summary>
    public static bool ApplyPlacement(IntPtr hwnd, int x, int y, int width, int height, int showState)
    {
        // 最小化要求は最小化だけして終了
        if (showState == SW_MINIMIZE)
        {
            ShowWindow(hwnd, SW_MINIMIZE);
            return true;
        }

        // 最小化・最大化状態から復元
        ShowWindow(hwnd, SW_RESTORE);
        System.Threading.Thread.Sleep(120);

        AppLogger.Instance.Debug("WindowLayout", "ApplyPlacement",
            $"目標 x={x} y={y} w={width} h={height}");

        // 1回目: 目標座標を外枠としてそのまま配置
        bool ok = SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
            SWP_NOZORDER | SWP_NOACTIVATE);

        // 2回目以降: 実測した見える枠と目標のズレを補正（最大3回で収束）
        for (int i = 0; i < 3; i++)
        {
            System.Threading.Thread.Sleep(120);

            bool gotVisible = TryGetVisibleRect(hwnd, out RECT visible);
            bool gotOuter   = GetWindowRect(hwnd, out RECT outer);

            if (!gotVisible || !gotOuter)
            {
                AppLogger.Instance.Debug("WindowLayout", "ApplyPlacement",
                    $"[{i}] 実測不可 visibleOk={gotVisible} outerOk={gotOuter} → 補正中断");
                break; // DWM 実測不可 → 素の配置のまま（過補正しない）
            }

            int visW = visible.Right - visible.Left;
            int visH = visible.Bottom - visible.Top;

            int errX = x - visible.Left;
            int errY = y - visible.Top;
            int errW = width  - visW;
            int errH = height - visH;

            AppLogger.Instance.Debug("WindowLayout", "ApplyPlacement",
                $"[{i}] outer=({outer.Left},{outer.Top},{outer.Right},{outer.Bottom}) " +
                $"visible=({visible.Left},{visible.Top},{visible.Right},{visible.Bottom}) " +
                $"err=({errX},{errY},{errW},{errH})");

            // 1px 以内に収束したら終了
            if (System.Math.Abs(errX) <= 1 && System.Math.Abs(errY) <= 1 &&
                System.Math.Abs(errW) <= 1 && System.Math.Abs(errH) <= 1)
            {
                AppLogger.Instance.Debug("WindowLayout", "ApplyPlacement", $"[{i}] 収束");
                break;
            }

            int newOuterX = outer.Left + errX;
            int newOuterY = outer.Top  + errY;
            int newOuterW = (outer.Right  - outer.Left) + errW;
            int newOuterH = (outer.Bottom - outer.Top)  + errH;

            SetWindowPos(hwnd, IntPtr.Zero, newOuterX, newOuterY, newOuterW, newOuterH,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        if (showState == SW_MAXIMIZE) ShowWindow(hwnd, SW_MAXIMIZE);
        return ok;
    }

    /// <summary>DWM 拡張フレーム境界（見える枠）を取得する。取得失敗・空矩形なら false。</summary>
    private static bool TryGetVisibleRect(IntPtr hwnd, out RECT visible)
    {
        visible = default;
        try
        {
            int size = Marshal.SizeOf<RECT>();
            if (DwmGetWindowAttributeRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out visible, size) != 0)
                return false;
            return visible.Right > visible.Left && visible.Bottom > visible.Top;
        }
        catch { return false; }
    }
}

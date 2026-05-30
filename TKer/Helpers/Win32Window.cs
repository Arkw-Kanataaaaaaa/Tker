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

    // Windows 11 標準の不可視ボーダー（DPI 100% 想定のフォールバック値）
    private const int FALLBACK_PAD_HORIZONTAL = 8;
    private const int FALLBACK_PAD_BOTTOM     = 8;
    private const int FALLBACK_PAD_TOP        = 0;

    /// <summary>
    /// 指定ハンドルのウィンドウを位置・サイズ・表示状態を指定して配置する（Windows 11 対応版）。
    /// 復元後に現ウィンドウから不可視ボーダー（シャドウ用余白）を実測し、
    /// 補正済みのサイズで「1回だけ」配置することで、配置後にサイズが伸びる
    /// ちらつきを起こさず、見える枠が指定座標ぴったりに来るようにする。
    /// </summary>
    public static bool ApplyPlacement(IntPtr hwnd, int x, int y, int width, int height, int showState)
    {
        // 最小化要求は最小化だけして終了
        if (showState == SW_MINIMIZE)
        {
            ShowWindow(hwnd, SW_MINIMIZE);
            return true;
        }

        // 最小化・最大化状態から復元（このあと不可視ボーダーを測れる状態にする）
        ShowWindow(hwnd, SW_RESTORE);
        System.Threading.Thread.Sleep(120);

        // 復元後の現ウィンドウから不可視ボーダーを測定（失敗時は Win11 標準値）
        MeasureInvisibleBorders(hwnd, out int leftPad, out int topPad, out int rightPad, out int bottomPad);

        int finalX = x - leftPad;
        int finalY = y - topPad;
        int finalW = width  + leftPad + rightPad;
        int finalH = height + topPad  + bottomPad;

        // 補正済みサイズで配置
        bool ok = SetWindowPos(hwnd, IntPtr.Zero, finalX, finalY, finalW, finalH,
            SWP_NOZORDER | SWP_NOACTIVATE);

        // Explorer / Edge など、配置後に自前の記憶サイズを非同期で復元して
        // 下方向などに伸びるアプリ対策として、同じ座標でもう一度確定させる。
        // 座標が同一なので行儀のよいアプリではちらつかない。
        System.Threading.Thread.Sleep(220);
        SetWindowPos(hwnd, IntPtr.Zero, finalX, finalY, finalW, finalH,
            SWP_NOZORDER | SWP_NOACTIVATE);

        if (showState == SW_MAXIMIZE) ShowWindow(hwnd, SW_MAXIMIZE);
        return ok;
    }

    /// <summary>
    /// 現ウィンドウの DWM 拡張フレーム境界と WindowRect の差から不可視ボーダー幅を測る。
    /// 取得失敗・異常値の辺は Windows 11 標準のフォールバック値を使う。
    /// </summary>
    private static void MeasureInvisibleBorders(IntPtr hwnd,
        out int leftPad, out int topPad, out int rightPad, out int bottomPad)
    {
        leftPad   = FALLBACK_PAD_HORIZONTAL;
        topPad    = FALLBACK_PAD_TOP;
        rightPad  = FALLBACK_PAD_HORIZONTAL;
        bottomPad = FALLBACK_PAD_BOTTOM;

        try
        {
            int size = Marshal.SizeOf<RECT>();
            if (DwmGetWindowAttributeRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT visible, size) == 0
                && GetWindowRect(hwnd, out RECT actual))
            {
                int lP = visible.Left  - actual.Left;
                int tP = visible.Top   - actual.Top;
                int rP = actual.Right  - visible.Right;
                int bP = actual.Bottom - visible.Bottom;
                // 左右下は Win11 では必ず数px のボーダーがあるため、3px 未満は
                // 測定失敗（0 が返る既知の挙動）とみなしてフォールバックを維持する。
                // 上辺は本来 0 のことが多いので 0 も妥当値として採用する。
                if (lP >= 3 && lP <= 30) leftPad   = lP;
                if (tP >= 0 && tP <= 30) topPad    = tP;
                if (rP >= 3 && rP <= 30) rightPad  = rP;
                if (bP >= 3 && bP <= 30) bottomPad = bP;
            }
        }
        catch { /* 実測失敗時はフォールバック */ }
    }
}

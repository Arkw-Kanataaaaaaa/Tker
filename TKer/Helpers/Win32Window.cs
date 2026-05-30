using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        placement.rcNormalPosition.Left   = x;
        placement.rcNormalPosition.Top    = y;
        placement.rcNormalPosition.Right  = x + width;
        placement.rcNormalPosition.Bottom = y + height;

        return SetWindowPlacement(hwnd, ref placement);
    }
}

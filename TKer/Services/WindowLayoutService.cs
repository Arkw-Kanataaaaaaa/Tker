using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using TKer.Helpers;
using TKer.Models;

namespace TKer.Services;

/// <summary>デスクトップ上のウィンドウ配置を保存・適用するサービス。</summary>
public class WindowLayoutService
{
    private static readonly string DATA_DIR =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TKer");
    private static readonly string DATA_FILE =
        Path.Combine(DATA_DIR, "window_layouts.json");

    private const int LAUNCH_WAIT_MS = 3000;
    private const int POLL_INTERVAL_MS = 300;

    private List<WindowLayout> _layouts;

    /// <summary>保存済みレイアウトを読み込んでサービスを初期化する。</summary>
    public WindowLayoutService()
    {
        _layouts = Load();
    }

    /// <summary>保存済みレイアウト一覧を作成日時の降順で返す。</summary>
    public IReadOnlyList<WindowLayout> All =>
        _layouts.OrderByDescending(l => l.CreatedAt).ToList();

    /// <summary>現在のデスクトップ可視ウィンドウから新規レイアウトを作成して保存する。</summary>
    public WindowLayout CaptureCurrent(string name, string description)
    {
        var windows = Win32Window.EnumerateVisibleWindows();
        var layout = new WindowLayout
        {
            Name        = name,
            Description = description,
            Windows = windows.Select(w => new WindowEntry
            {
                Title     = w.Title,
                ExePath   = w.ExePath,
                ClassName = w.ClassName,
                X         = w.X,
                Y         = w.Y,
                Width     = w.Width,
                Height    = w.Height,
                ShowState = w.ShowState
            }).ToList()
        };
        _layouts.Add(layout);
        Save();
        return layout;
    }

    /// <summary>名前・説明・エントリ一覧を指定して新規レイアウトを作成・保存する。</summary>
    public WindowLayout Create(string name, string description, List<WindowEntry> entries)
    {
        var layout = new WindowLayout
        {
            Name        = name,
            Description = description,
            Windows     = entries
        };
        _layouts.Add(layout);
        Save();
        return layout;
    }

    /// <summary>名前・説明を更新したレイアウトを保存する。</summary>
    public void Update(WindowLayout layout)
    {
        var idx = _layouts.FindIndex(l => l.Id == layout.Id);
        if (idx < 0) return;
        layout.UpdatedAt = DateTime.Now;
        _layouts[idx] = layout;
        Save();
    }

    /// <summary>指定 ID のレイアウトを削除して保存する。</summary>
    public void Delete(string id)
    {
        _layouts.RemoveAll(l => l.Id == id);
        Save();
    }

    /// <summary>
    /// レイアウトを現在のデスクトップに適用する。
    /// 未起動のウィンドウは実行ファイルを起動して最大 LAUNCH_WAIT_MS 待ってから配置する。
    /// 成功・失敗それぞれのエントリのタイトル一覧を返す。
    /// </summary>
    public ApplyResult Apply(WindowLayout layout)
    {
        var result = new ApplyResult();
        // 既に何かの配置で割り当てたハンドルは再利用しない（同一exe複数インスタンスの取り違え対策）
        var usedHandles = new HashSet<IntPtr>();

        // 他ウィンドウ最小化オプション
        if (layout.MinimizeOthers) MinimizeAllExceptSelf();

        foreach (var entry in layout.Windows)
        {
            try
            {
                var hwnd = FindWindowForEntry(entry, usedHandles);
                if (hwnd == IntPtr.Zero)
                {
                    if (!TryLaunch(entry.ExePath))
                    {
                        result.Failed.Add(entry.Title);
                        continue;
                    }
                    // 起動後ポーリング待機
                    var deadline = DateTime.UtcNow.AddMilliseconds(LAUNCH_WAIT_MS);
                    while (DateTime.UtcNow < deadline)
                    {
                        Thread.Sleep(POLL_INTERVAL_MS);
                        hwnd = FindWindowForEntry(entry, usedHandles);
                        if (hwnd != IntPtr.Zero) break;
                    }
                    if (hwnd == IntPtr.Zero) { result.Failed.Add(entry.Title); continue; }
                }

                usedHandles.Add(hwnd);
                if (Win32Window.ApplyPlacement(hwnd, entry.X, entry.Y, entry.Width, entry.Height, entry.ShowState))
                    result.Succeeded.Add(entry.Title);
                else
                    result.Failed.Add(entry.Title);
            }
            catch
            {
                result.Failed.Add(entry.Title);
            }
        }
        return result;
    }

    /// <summary>
    /// エントリと一致するウィンドウハンドルを探して返す。
    /// exe パス一致 → タイトル完全一致 → タイトル前方一致 の順で絞り込む。
    /// </summary>
    private static IntPtr FindWindowForEntry(WindowEntry entry, HashSet<IntPtr> exclude)
    {
        var candidates = Win32Window.EnumerateVisibleWindows()
            .Where(w => !exclude.Contains(w.Handle))
            .Where(w => string.Equals(w.ExePath, entry.ExePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0) return IntPtr.Zero;
        if (candidates.Count == 1) return candidates[0].Handle;

        var exact = candidates.FirstOrDefault(c =>
            string.Equals(c.Title, entry.Title, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.Handle;

        if (!string.IsNullOrEmpty(entry.Title))
        {
            var prefix = entry.Title.Split(' ')[0];
            var byPrefix = candidates.FirstOrDefault(c =>
                c.Title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (byPrefix != null) return byPrefix.Handle;
        }

        return candidates[0].Handle;
    }

    /// <summary>TKer 本体ウィンドウを除く全ての可視ウィンドウを最小化する。</summary>
    private static void MinimizeAllExceptSelf()
    {
        IntPtr selfHwnd = IntPtr.Zero;
        try
        {
            var mw = System.Windows.Application.Current?.MainWindow;
            if (mw != null) selfHwnd = new System.Windows.Interop.WindowInteropHelper(mw).Handle;
        }
        catch { }

        foreach (var w in Win32Window.EnumerateVisibleWindows())
        {
            if (w.Handle == selfHwnd) continue;
            try { Win32Window.MinimizeWindow(w.Handle); } catch { }
        }
    }

    /// <summary>実行ファイルを起動する。失敗時は false を返す。</summary>
    private static bool TryLaunch(string exePath)
    {
        try
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private List<WindowLayout> Load()
    {
        try
        {
            if (File.Exists(DATA_FILE))
                return JsonConvert.DeserializeObject<List<WindowLayout>>(File.ReadAllText(DATA_FILE))
                       ?? new List<WindowLayout>();
        }
        catch { /* 読み込み失敗時は新規 */ }
        return new List<WindowLayout>();
    }

    private void Save()
    {
        Directory.CreateDirectory(DATA_DIR);
        var json = JsonConvert.SerializeObject(_layouts, Formatting.Indented);
        File.WriteAllText(DATA_FILE, json);
    }
}

/// <summary>ウィンドウレイアウト適用の結果（成功・失敗それぞれのタイトル一覧）。</summary>
public class ApplyResult
{
    /// <summary>配置に成功したエントリのタイトル一覧。</summary>
    public List<string> Succeeded { get; } = new();
    /// <summary>配置に失敗したエントリのタイトル一覧。</summary>
    public List<string> Failed    { get; } = new();
}

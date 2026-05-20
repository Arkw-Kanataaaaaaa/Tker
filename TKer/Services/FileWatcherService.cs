using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// タスクフォルダ内のファイル変更を監視し、
/// 変更検知時に TaskFileChanged イベントを発火するサービス。
/// </summary>
public sealed class FileWatcherService : IDisposable
{
    // taskId → FileSystemWatcher
    private readonly Dictionary<string, FileSystemWatcher> _watchers  = new();
    // taskId → debounce timer
    private readonly Dictionary<string, Timer>             _timers    = new();
    // taskId → last changed file path
    private readonly Dictionary<string, string>            _lastFile  = new();

    private readonly object _lock = new();
    private bool _disposed = false;

    /// <summary>
    /// taskId: 変更されたタスクID、filePath: 変更されたファイルのフルパス
    /// ※ UI スレッドからではなくワーカースレッドから発火することがある。
    ///   ハンドラ内で Dispatcher.Invoke すること。
    /// </summary>
    public event Action<string, string>? TaskFileChanged;

    // ── 監視開始 ─────────────────────────────────────────
    /// <summary>指定タスクのフォルダ監視を開始する。</summary>
    public void WatchTask(TaskItem task)
    {
        if (string.IsNullOrEmpty(task.FolderPath) || !Directory.Exists(task.FolderPath))
            return;

        lock (_lock)
        {
            if (_watchers.ContainsKey(task.Id)) return; // already watching

            var w = new FileSystemWatcher(task.FolderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter          = NotifyFilters.LastWrite | NotifyFilters.FileName
                                       | NotifyFilters.DirectoryName | NotifyFilters.Size,
                EnableRaisingEvents   = true
            };

            var taskId   = task.Id;
            w.Changed += (_, e) => OnChanged(taskId, e.FullPath);
            w.Created += (_, e) => OnChanged(taskId, e.FullPath);
            w.Renamed += (_, e) => OnChanged(taskId, e.FullPath);

            _watchers[taskId] = w;
        }
    }

    /// <summary>指定タスク ID のフォルダ監視を停止する。</summary>
    public void StopWatching(string taskId)
    {
        lock (_lock)
        {
            if (_watchers.TryGetValue(taskId, out var w))
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
                _watchers.Remove(taskId);
            }
            if (_timers.TryGetValue(taskId, out var t))
            {
                t.Dispose();
                _timers.Remove(taskId);
            }
            _lastFile.Remove(taskId);
        }
    }

    /// <summary>全タスクを再監視。既存ウォッチャーを破棄してから設定。</summary>
    public void RestartAll(IEnumerable<TaskItem> tasks)
    {
        lock (_lock)
        {
            foreach (var w in _watchers.Values)
            {
                try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
            }
            foreach (var t in _timers.Values) try { t.Dispose(); } catch { }
            _watchers.Clear();
            _timers.Clear();
            _lastFile.Clear();
        }
        foreach (var task in tasks)
            WatchTask(task);
    }

    // ── イベントハンドラ（ファイル変更検知）────────────
    /// <summary>ファイル変更イベントを受け取り、デバウンス付きで処理する。</summary>
    private void OnChanged(string taskId, string filePath)
    {
        if (_disposed) return;

        // project_data.json や .bak は無視
        var name = Path.GetFileName(filePath);
        if (name.EndsWith(".json",     StringComparison.OrdinalIgnoreCase)) return;
        if (name.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase)) return;
        if (name.StartsWith("PFTMP_",  StringComparison.OrdinalIgnoreCase)) return;

        lock (_lock)
        {
            _lastFile[taskId] = filePath;

            if (_timers.TryGetValue(taskId, out var existing))
            {
                // debounce: reset timer
                existing.Change(600, Timeout.Infinite);
            }
            else
            {
                _timers[taskId] = new Timer(_ => FireEvent(taskId), null, 600, Timeout.Infinite);
            }
        }
    }

    /// <summary>デバウンス後に TaskFileChanged イベントを発火する。</summary>
    private void FireEvent(string taskId)
    {
        string? fp;
        lock (_lock)
        {
            _lastFile.TryGetValue(taskId, out fp);
            if (_timers.TryGetValue(taskId, out var t))
            {
                t.Dispose();
                _timers.Remove(taskId);
            }
        }
        if (fp != null)
            TaskFileChanged?.Invoke(taskId, fp);
    }

    /// <summary>全ウォッチャーとタイマーを破棄する。</summary>
    public void Dispose()
    {
        _disposed = true;
        lock (_lock)
        {
            foreach (var w in _watchers.Values) try { w.Dispose(); } catch { }
            foreach (var t in _timers.Values)   try { t.Dispose(); } catch { }
            _watchers.Clear();
            _timers.Clear();
        }
    }
}

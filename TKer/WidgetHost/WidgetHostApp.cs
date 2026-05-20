using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using TKer.Services;
using TKer.Views.Widgets;

namespace TKer.WidgetHost;

/// <summary>
/// ウィジェット専用プロセスのエントリポイント管理クラス。
/// --widget フラグで起動された時のみ使用される。
/// システムトレイアイコン + BookmarkWidget を管理する。
/// WinForms 非依存 — Hardcodet.NotifyIcon.Wpf (TaskbarIcon) を使用。
/// </summary>
public class WidgetHostApp : IDisposable
{
    // ── Mutex（二重起動防止） ─────────────────────────────
    private static readonly string MutexName = "TKer.WidgetProcess.v1";
    private Mutex? _mutex;

    // ── サービス ─────────────────────────────────────────
    private readonly AppSettingsService _appSettings;
    private readonly ScheduleService    _schedule;
    private readonly ProjectService     _project;
    private readonly TodoService        _todo;

    // ── ウィジェット ─────────────────────────────────────
    private BookmarkWidget?  _bookmarkWidget;

    // ── システムトレイ（WPF ネイティブ） ──────────────────
    private TaskbarIcon? _trayIcon;

    // ── ファイル監視 ─────────────────────────────────────
    private FileSystemWatcher? _settingsWatcher;
    private FileSystemWatcher? _todoWatcher;
    private FileSystemWatcher? _scheduleWatcher;
    private DispatcherTimer?   _reloadTimer;   // デバウンス用

    // ── アプリデータディレクトリ ──────────────────────────
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TKer");

    // ── 公開：二重起動チェック ────────────────────────────
    public static bool IsAlreadyRunning()
    {
        var mutex = new Mutex(true, MutexName, out bool created);
        if (!created) { mutex.Dispose(); return true; }
        mutex.Dispose();
        return false;
    }

    // ── コンストラクタ ────────────────────────────────────
    public WidgetHostApp()
    {
        _mutex = new Mutex(true, MutexName, out bool created);
        if (!created) { _mutex.Dispose(); _mutex = null; }

        _appSettings = new AppSettingsService();
        _schedule    = new ScheduleService();
        _project     = new ProjectService();
        _todo        = new TodoService();

        var lastPath = _appSettings.LastOpenedProjectPath;
        if (!string.IsNullOrEmpty(lastPath) && File.Exists(lastPath))
        {
            try { _project.LoadProject(lastPath); }
            catch { /* 読み込み失敗は無視 */ }
        }

        AppLogger.Instance.Configure(_appSettings.LogRotation);
        AppLogger.Instance.Info("WidgetHostApp", ".ctor", "ウィジェットプロセス起動");
    }

    // ── 起動 ─────────────────────────────────────────────
    public void Start()
    {
        BuildTrayIcon();

        var svcProvider = new WidgetServiceProvider(
            _appSettings, _schedule, _project, _todo,
            view => LaunchMainApp());

        _bookmarkWidget = new BookmarkWidget(svcProvider);
        _bookmarkWidget.ShowWidget();

        StartFileWatchers();

        AppLogger.Instance.Info("WidgetHostApp", "Start", "ウィジェット起動完了");
    }

    // ── システムトレイアイコン構築（WPF ネイティブ） ──────
    private void BuildTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "TKer ウィジェット"
        };

        // アイコン: 実行ファイルから取得 (System.Drawing は System.Drawing.Common で提供)
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (File.Exists(exePath))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) _trayIcon.Icon = icon;
            }
        }
        catch { /* アイコン取得失敗は無視 */ }

        // WPF ContextMenu（WinForms 不要）
        var menu = new ContextMenu();

        var itemBookmark = new MenuItem { Header = "🔖 栞ウィジェット" };
        itemBookmark.Click += (_, _) => ToggleBookmarkWidget();

        var itemOpenMain = new MenuItem { Header = "🚀 TKer を開く" };
        itemOpenMain.Click += (_, _) => LaunchMainApp();

        var itemExit = new MenuItem { Header = "✕ 終了" };
        itemExit.Click += (_, _) => ExitApp();

        menu.Items.Add(itemBookmark);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemOpenMain);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemExit);

        _trayIcon.ContextMenu = menu;

        // ダブルクリックで栞ウィジェット表示切替
        _trayIcon.TrayMouseDoubleClick += (_, _) => ToggleBookmarkWidget();

        // 起動通知バルーン
        _trayIcon.ShowBalloonTip(
            "TKer ウィジェット",
            "ウィジェットが起動しました。トレイアイコンから操作できます。",
            BalloonIcon.Info);
    }

    // ── ウィジェット表示切替 ──────────────────────────────
    private void ToggleBookmarkWidget()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_bookmarkWidget == null) return;
            if (_bookmarkWidget.IsVisible)
                _bookmarkWidget.Hide();
            else
                _bookmarkWidget.ShowWidget();
        });
    }

    // ── メインアプリの起動 ────────────────────────────────
    private static void LaunchMainApp()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (!string.IsNullOrEmpty(exePath))
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }
        catch { /* 無視 */ }
    }

    // ── アプリ終了 ────────────────────────────────────────
    private void ExitApp()
    {
        AppLogger.Instance.Info("WidgetHostApp", "ExitApp", "ウィジェットプロセス終了");
        _trayIcon?.Dispose();
        _bookmarkWidget?.ForceClose();
        Application.Current.Shutdown();
    }

    // ── ファイル変更監視 ──────────────────────────────────
    private void StartFileWatchers()
    {
        if (!Directory.Exists(AppDataDir)) return;

        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); OnFilesChanged(); };

        WatchFile("settings.json",  ref _settingsWatcher);
        WatchFile("todos.json",     ref _todoWatcher);
        WatchFile("schedule.json",  ref _scheduleWatcher);
    }

    private void WatchFile(string fileName, ref FileSystemWatcher? watcher)
    {
        try
        {
            watcher = new FileSystemWatcher(AppDataDir, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnDataFileChanged;
        }
        catch { /* 監視失敗は無視 */ }
    }

    private void OnDataFileChanged(object sender, FileSystemEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _reloadTimer?.Stop();
            _reloadTimer?.Start();
        });
    }

    private void OnFilesChanged()
    {
        AppLogger.Instance.Debug("WidgetHostApp", "OnFilesChanged", "設定ファイル変更を検出・リロード");
        // ウィジェットが表示中ならリフレッシュ
        _bookmarkWidget?.Dispatcher.Invoke(() =>
        {
            if (_bookmarkWidget.IsVisible)
                _bookmarkWidget.InvalidateVisual();
        });
    }

    // ── IDisposable ──────────────────────────────────────
    public void Dispose()
    {
        _settingsWatcher?.Dispose();
        _todoWatcher?.Dispose();
        _scheduleWatcher?.Dispose();
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}

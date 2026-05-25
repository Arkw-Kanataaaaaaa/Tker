using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TKer.Models;
using TKer.Services;
using TKer.Views.Dialogs;

namespace TKer.ViewModels;

/// <summary>アプリケーション全体のナビゲーションと状態を管理するメイン ViewModel。</summary>
public partial class MainViewModel : ObservableObject
{
    // ── サービス ──────────────────────────────────
    /// <summary>プロジェクトデータの読み書きを担うサービス。</summary>
    public ProjectService         ProjectService         { get; }
    /// <summary>プロジェクト初期セットアップを担うサービス。</summary>
    public SetupService           SetupService           { get; }
    /// <summary>成果物管理を担うサービス。</summary>
    public DeliverableService     DeliverableService     { get; }
    /// <summary>アプリ設定の読み書きを担うサービス。</summary>
    public AppSettingsService     AppSettingsService     { get; }
    /// <summary>コレクション機能を担うサービス。</summary>
    public CollectionService      CollectionService      { get; }
    /// <summary>スケジュールイベントの管理を担うサービス。</summary>
    public ScheduleService        ScheduleService        { get; }
    /// <summary>記事作成機能を担うサービス。</summary>
    public ArticleService         ArticleService         { get; }
    /// <summary>ファイル変更監視を担うサービス。</summary>
    public FileWatcherService     FileWatcherService     { get; }
    /// <summary>TODO リストの管理を担うサービス。</summary>
    public TodoService            TodoService            { get; }
    /// <summary>プロジェクトフォルダの整合性チェックを担うサービス。</summary>
    public FolderIntegrityService FolderIntegrityService { get; }

    // ── ナビゲーション ────────────────────────────
    [ObservableProperty] private string _currentView = "Home";
    [ObservableProperty] private string _projectTitle = "TKer";
    [ObservableProperty] private string _statusMessage = "ホームからプロジェクトを選択してください";
    [ObservableProperty] private bool   _isProjectLoaded = false;

    /// <summary>カレンダー画面でフォーカスする日付。null の場合は今日。</summary>
    public DateTime? CalendarFocusDate { get; set; }

    /// <summary>コレクション詳細ページで表示するコレクション。</summary>
    public Collection? SelectedCollection { get; set; }

    // ── 統計（現在プロジェクト） ─────────────────
    [ObservableProperty] private int _totalTasks    = 0;
    [ObservableProperty] private int _doneTasks     = 0;
    [ObservableProperty] private int _wipTasks      = 0;
    [ObservableProperty] private int _todoTasks     = 0;
    [ObservableProperty] private int _categoryCount = 0;

    // ── アラートバッジ（全プロジェクト横断） ─────
    [ObservableProperty] private int _alertCount = 0;

    // ── フォルダ操作エラー通知 ─────────────────────
    [ObservableProperty] private string? _folderOpError;
    [ObservableProperty] private bool    _hasFolderOpError;

    // ── フォルダキュー処理状態 ─────────────────────
    [ObservableProperty] private bool    _isFolderQueueBusy;
    [ObservableProperty] private int     _folderQueueCount;

    // ── ファイル変更通知（進捗反映タグ） ────────────
    [ObservableProperty] private string? _fileChangeTaskName;
    [ObservableProperty] private string? _fileChangeTaskId;
    [ObservableProperty] private string? _fileChangeFilePath;
    [ObservableProperty] private bool    _hasFileChangeNotification;

    // ── バージョン ────────────────────────────────
    /// <summary>表示用のアプリバージョン文字列。</summary>
    public string AppVersionText => AppVersion.DISPLAY_NAME;
    /// <summary>表示用のビルド日付文字列。</summary>
    public string BuildDateText  => $"Build {AppVersion.BUILD_DATE}";

    /// <summary>各サービスを初期化し、イベントハンドラーを登録するコンストラクター。</summary>
    public MainViewModel()
    {
        AppSettingsService     = new AppSettingsService();
        ProjectService         = new ProjectService();
        SetupService           = new SetupService(ProjectService);
        DeliverableService     = new DeliverableService(ProjectService);
        CollectionService      = new CollectionService();
        ScheduleService        = new ScheduleService();
        ArticleService         = new ArticleService();
        FileWatcherService     = new FileWatcherService();
        TodoService            = new TodoService();
        FolderIntegrityService = new FolderIntegrityService(ProjectService);

        // ロガー設定の適用
        AppLogger.Instance.Configure(AppSettingsService.LogRotation);
        AppLogger.Instance.Info("MainViewModel", ".ctor", "アプリ起動");

        // フォルダ整合性チェック開始
        FolderIntegrityService.Start();

        // フォルダ操作エラーを UI に通知
        ProjectService.FolderQueue.OperationFailed += (label, ex) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                FolderOpError    = $"⚠ {label}\n{ex.Message}";
                HasFolderOpError = true;
                // 8 秒後に自動消去
                var timer = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(8) };
                timer.Tick += (_, _) =>
                {
                    FolderOpError    = null;
                    HasFolderOpError = false;
                    timer.Stop();
                };
                timer.Start();
            });
        };

        // フォルダキュー件数変化を UI に通知
        ProjectService.FolderQueue.PendingCountChanged += count =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                FolderQueueCount   = count;
                IsFolderQueueBusy  = count > 0;
            });
        };

        // ── ファイル変更通知（進捗反映タグ）─────────
        FileWatcherService.TaskFileChanged += (taskId, filePath) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var task = ProjectService.CurrentProject?.Tasks.FirstOrDefault(t => t.Id == taskId);
                if (task == null) return;

                // ProgressTagFiles が設定されている場合、タグ付きファイルのみ通知
                if (task.ProgressTagFiles.Count > 0 &&
                    !task.ProgressTagFiles.Any(f =>
                        string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase)))
                    return;

                FileChangeTaskId   = taskId;
                FileChangeTaskName = task.Name;
                FileChangeFilePath = filePath;
                HasFileChangeNotification = true;

                // 30 秒後に自動消去
                var timer = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(30) };
                timer.Tick += (_, _) =>
                {
                    HasFileChangeNotification = false;
                    FileChangeTaskName = null;
                    FileChangeFilePath = null;
                    timer.Stop();
                };
                timer.Start();
            });
        };

        ProjectService.ProjectChanged += (_, _) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                RefreshStats();
                IsProjectLoaded = ProjectService.IsLoaded;
                if (ProjectService.CurrentProject != null)
                {
                    ProjectTitle = ProjectService.CurrentProject.Settings.ProjectName;
                    StatusMessage = $"プロジェクト: {ProjectTitle}";
                    // 最近使ったプロジェクトに登録
                    AppSettingsService.RegisterProject(
                        ProjectService.ProjectFilePath!,
                        ProjectService.CurrentProject.Settings.ProjectName,
                        ProjectService.CurrentProject.Settings.ProjectPath,
                        ProjectService.CurrentProject.Settings.Description);

                    // ファイル監視を再起動
                    var taskList = ProjectService.CurrentProject.Tasks.ToList();
                    System.Threading.Tasks.Task.Run(() =>
                        FileWatcherService.RestartAll(taskList));
                }
                RefreshAlerts();
            });
        };

        // 起動時: 最後に開いたプロジェクトを自動復元（ホーム画面から開始）
        var lastPath = AppSettingsService.LastOpenedProjectPath;
        if (!string.IsNullOrEmpty(lastPath) && System.IO.File.Exists(lastPath))
        {
            ProjectService.LoadProject(lastPath);
        }

        // 起動時アラート集計
        RefreshAlerts();
    }

    // ── ナビゲーション ────────────────────────────
    /// <summary>指定したビューに遷移する。プロジェクト未ロード時は一部ビューのみ許可。</summary>
    [RelayCommand]
    public void NavigateTo(string view)
    {
        // Home / Setup / EnvSetup / ProjectList / ツール系はプロジェクト未ロードでも開ける
        var noAuthViews = new[] { "Home", "Setup", "Shortcuts", "ProjectList", "AppSettings",
                                   "UiCustomize", "Pomodoro", "Article", "Collection", "CollectionItems" };
        if (!IsProjectLoaded && !noAuthViews.Contains(view)) return;
        CurrentView = view;
    }

    // ── 保存 ─────────────────────────────────────
    /// <summary>現在のプロジェクトをファイルに保存する。</summary>
    [RelayCommand]
    public void Save()
    {
        if (!IsProjectLoaded) return;
        ProjectService.SaveProject();
        StatusMessage = $"保存しました ({DateTime.Now:HH:mm:ss})";
    }

    // ── プロジェクト切替 ──────────────────────────
    /// <summary>指定したデータファイルパスのプロジェクトに切り替える。</summary>
    [RelayCommand]
    public void SwitchProject(string dataFilePath)
    {
        if (ProjectService.LoadProject(dataFilePath))
        {
            NavigateTo("Home");
        }
        else
        {
            AppSettingsService.RemoveProject(dataFilePath);
            MessageBox.Show("プロジェクトファイルが見つかりません。\n一覧から削除しました。",
                "読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshAlerts();
        }
    }


    // ── アラートダイアログ ────────────────────────
    /// <summary>全プロジェクトのアラート一覧ダイアログを表示する。</summary>
    [RelayCommand]
    public void ShowAlerts()
    {
        var dlg = new TKer.Views.Dialogs.AlertListDialog(AppSettingsService, this);
        dlg.Owner = Application.Current.MainWindow;
        dlg.ShowDialog();
    }

    // ── アラート更新 ──────────────────────────────
    /// <summary>全プロジェクトのアラート件数を再集計して AlertCount を更新する。</summary>
    public void RefreshAlerts()
    {
        var alerts = AppSettingsService.CollectAlerts();
        AlertCount = alerts.Count;
    }

    // ── 統計更新 ──────────────────────────────────
    /// <summary>現在のプロジェクトのタスク統計とカテゴリー数を再計算して各プロパティを更新する。</summary>
    private void RefreshStats()
    {
        var (total, done, wip, todo) = ProjectService.GetTaskStats();
        TotalTasks    = total;
        DoneTasks     = done;
        WipTasks      = wip;
        TodoTasks     = todo;
        CategoryCount = ProjectService.CurrentProject?.Categories.Count ?? 0;
    }
}

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TKer.ViewModels;
using TKer.Views.Pages;
using TKer.Views.Widgets;

namespace TKer.Views;

/// <summary>アプリケーションのメインウィンドウ。ナビゲーション・ウィジェット・ウィンドウ操作を統括する。</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    // ── 外部から直接メソッド呼び出しが必要なページのみフィールドで保持 ──────
    // 遷移のたびに新しいインスタンスに更新される
    private TaskListPage _taskListPage = null!;

    // ── ウィジェット ────────────────────────────────────────
    private BookmarkWidget?  _bookmarkWidget;

    /// <summary>メインウィンドウを初期化し、ViewModelのバインド・背景・ウィジェットを設定する。</summary>
    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        ApplyMenuOrder();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentView))
                NavigateToCurrentView();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm.SaveCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm.NavigateToCommand.Execute("Home");
                e.Handled = true;
            }
        };

        App.ApplyFont(_vm.AppSettingsService.Theme.FontFamily, this);
        ApplyBackground();
        NavigateToCurrentView();

        // Windows 標準アニメーションを有効化（SourceInitialized 後に実行）
        SourceInitialized += (_, _) => EnableNativeAnimations();

        // ウィジェット初期化
        InitBookmarkWidget();
    }

    // ── 動画壁紙の拡張子リスト ────────────────────────────
    private static readonly string[] VIDEO_EXTENSIONS =
        { ".mp4", ".avi", ".wmv", ".mov", ".mkv", ".webm" };

    /// <summary>設定に基づいて動画・画像・無地の背景をウィンドウに適用する。</summary>
    public void ApplyBackground()
    {
        var path = _vm.AppSettingsService.BackgroundImagePath;

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (Array.Exists(VIDEO_EXTENSIONS, e => e == ext))
            {
                // ── 動画壁紙 ──
                Background = (System.Windows.Media.Brush)FindResource("BgPrimaryBrush");
                BgVideoElement.Source   = new System.Uri(path, System.UriKind.Absolute);
                BgVideoElement.Volume   = 0;
                BgVideoElement.IsMuted  = true;
                BgVideoElement.Opacity  = 1.0;
                BgVideoElement.Play();
                BgVideoElement.MediaEnded -= BgVideo_MediaEnded;
                BgVideoElement.MediaEnded += BgVideo_MediaEnded;
            }
            else
            {
                // ── 画像壁紙 ──
                StopBgVideo();
                var bmp = new BitmapImage(new System.Uri(path, System.UriKind.Absolute));
                Background = new System.Windows.Media.ImageBrush(bmp)
                {
                    Stretch = System.Windows.Media.Stretch.UniformToFill,
                    Opacity = 1.0
                };
            }
        }
        else
        {
            // ── 壁紙なし ──
            StopBgVideo();
            Background = (System.Windows.Media.Brush)FindResource("BgPrimaryBrush");
        }
    }

    /// <summary>動画壁紙が終端に達したときにループ再生を開始する。</summary>
    private void BgVideo_MediaEnded(object sender, System.Windows.RoutedEventArgs e)
    {
        // ループ再生
        BgVideoElement.Position = TimeSpan.Zero;
        BgVideoElement.Play();
    }

    /// <summary>動画壁紙の再生を停止し、ソースをクリアして非表示にする。</summary>
    private void StopBgVideo()
    {
        BgVideoElement.MediaEnded -= BgVideo_MediaEnded;
        BgVideoElement.Stop();
        BgVideoElement.Source  = null;
        BgVideoElement.Opacity = 0;
    }

    // ────────────────────────────────────────────────────────
    // 画面遷移：遷移のたびにページを new して完全初期化する
    // ────────────────────────────────────────────────────────
    private bool _isNavigating = false;

    /// <summary>現在のビュー名に対応するページを生成し、フェードアニメーション付きで画面遷移する。</summary>
    private void NavigateToCurrentView()
    {
        if (_isNavigating) return;
        _isNavigating = true;

        // 毎回新しいインスタンスを生成（完全初期化）
        // 外部メソッド呼び出しが必要なページは該当フィールドも同時に更新する
        Page page = CreateFreshPage(_vm.CurrentView);

        // オーバーレイを即時表示
        LoadingOverlay.BeginAnimation(OpacityProperty, null);
        LoadingOverlay.Opacity    = 1;
        LoadingOverlay.Visibility = Visibility.Visible;

        // Background 優先度で遅延実行してオーバーレイを先に描画させる
        Dispatcher.InvokeAsync(() =>
        {
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(110))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) =>
            {
                // カレンダーページへの日付フォーカス
                if (page is CalendarPage cp && _vm.CalendarFocusDate.HasValue)
                {
                    cp.NavigateTo(_vm.CalendarFocusDate.Value);
                    _vm.CalendarFocusDate = null;
                }

                // 新インスタンスでも Refresh() を呼んでデータを確実に読み込む
                (page as IRefreshable)?.Refresh();
                MainFrame.Navigate(page);

                var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                fadeIn.Completed += (_, _) =>
                {
                    _isNavigating = false;
                    HideLoadingOverlay();
                };
                MainFrame.BeginAnimation(OpacityProperty, fadeIn);
            };
            MainFrame.BeginAnimation(OpacityProperty, fadeOut);

        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 遷移先ビュー名に対応する新しいページインスタンスを生成して返す。
    /// 外部メソッド呼び出しが必要なページ（TaskListPage）は
    /// フィールドも同時に更新するため、呼び出し後は最新インスタンスを参照できる。
    /// </summary>
    private Page CreateFreshPage(string view) => view switch
    {
        "Home"                => new HomePage(_vm),
        "Dashboard"           => new DashboardPage(_vm),
        "Project"             => new ProjectListPage(_vm),  // ProjectPage は廃止
        "Category"            => (_taskListPage  = new TaskListPage(_vm)),
        "TaskList"            => (_taskListPage  = new TaskListPage(_vm)),
        "Gantt"               => (_taskListPage  = new TaskListPage(_vm)),
        "Calendar"            => new CalendarPage(_vm),

        "Deliverable"         => new DeliverablePage(_vm),
        "Shortcuts"           => new ShortcutsPage(_vm),
        "ProjectList"         => new ProjectListPage(_vm),
        "AppSettings"         => new AppSettingsPage(_vm),
        "Collection"          => new CollectionPage(_vm),
        "UiCustomize"         => new UiCustomizePage(_vm),
        "Pomodoro"            => new PomodoroPage(_vm),
        "Article"             => new ArticlePage(_vm),

        "LogViewer"           => new LogViewerPage(_vm),
        "Todo"                => new TodoPage(_vm),
        "Setup"               => new ProjectListPage(_vm),
        _                     => new HomePage(_vm),
    };

    /// <summary>ローディングオーバーレイをフェードアウトアニメーションで非表示にする。</summary>
    private void HideLoadingOverlay()
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) =>
        {
            LoadingOverlay.BeginAnimation(OpacityProperty, null);
            LoadingOverlay.Visibility = Visibility.Collapsed;
        };
        LoadingOverlay.BeginAnimation(OpacityProperty, anim);
    }

    // ── ファイル変更通知クリック ─────────────────────────────
    /// <summary>ファイル変更通知トーストをクリックしたときにクイックステータスダイアログを開く。</summary>
    private void FileChangeToast_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var taskId = _vm.FileChangeTaskId;
        if (string.IsNullOrEmpty(taskId)) return;

        var task = _vm.ProjectService.CurrentProject?.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null) return;

        _vm.HasFileChangeNotification = false;

        var dlg = new Views.Dialogs.QuickStatusDialog(task) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            task.Status = dlg.NewStatus;
            if (!string.IsNullOrEmpty(dlg.NewNotes))
                task.Notes = (string.IsNullOrEmpty(task.Notes) ? "" : task.Notes + "\n") +
                             $"[{DateTime.Now:MM/dd HH:mm}] {dlg.NewNotes}";
            task.UpdatedAt = DateTime.Now;
            _vm.ProjectService.MarkDirtyAndSave();
        }
    }

    private void AppSettings_Click(object sender, System.Windows.RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("AppSettings");

    /// <summary>バージョン情報ダイアログを表示する。</summary>
    private void MenuAbout_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        MessageBox.Show(
            $"{TKer.Models.AppVersion.DISPLAY_NAME}\nBuild {TKer.Models.AppVersion.BUILD_DATE}\n\n© 2025 TKer",
            "バージョン情報",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void MenuChangelog_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Views.Dialogs.ChangelogDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void MenuManual_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Views.Dialogs.ManualDialog { Owner = this };
        dlg.ShowDialog();
    }

    /// <summary>タスクリストページへ遷移してカテゴリ追加ダイアログを開く。</summary>
    private void MenuNewCategory_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _vm.NavigateToCommand.Execute("TaskList");
        _taskListPage.AddCategory_Click(this, new System.Windows.RoutedEventArgs());
    }

    /// <summary>タスクリストページへ遷移してタスク追加ダイアログを開く。</summary>
    private void MenuNewTask_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _vm.NavigateToCommand.Execute("TaskList");
        _taskListPage.TriggerAddDialog();
    }

    private void MenuExportCsv_Click(object sender, System.Windows.RoutedEventArgs e)
        => _taskListPage.TriggerExportCsv();

    private void MenuExportWbs_Click(object sender, System.Windows.RoutedEventArgs e)
        => _taskListPage.TriggerExportWbs();

    /// <summary>新規プロジェクト作成ダイアログを開き、カテゴリテンプレートを適用してプロジェクト一覧へ遷移する。</summary>
    private void NewProject_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Views.Dialogs.NewProjectDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        _vm.ProjectService.CreateProject(dlg.SavePath, dlg.ProjectName, dlg.Description);

        var customPresets = _vm.AppSettingsService.Settings.CategoryPresets;
        var templateDlg = new Views.Dialogs.CategoryTemplateDialog(customPresets) { Owner = this };
        if (templateDlg.ShowDialog() == true)
        {
            foreach (var item in templateDlg.SelectedCategories)
                _vm.ProjectService.AddCategory(item.Name, item.Description, item.Color);
        }

        _vm.NavigateToCommand.Execute("ProjectList");
    }

    /// <summary>ファイル選択ダイアログでプロジェクトファイルを選択して読み込む。</summary>
    private void OpenProject_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "プロジェクトファイルを開く",
            Filter = "TKer データ (*.json)|*.json|すべてのファイル (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            _vm.SwitchProjectCommand.Execute(dlg.FileName);
    }

    /// <summary>設定に保存されたメニュー順序に従い、トップメニューの項目を並び替える。</summary>
    public void ApplyMenuOrder()
    {
        var order = _vm.AppSettingsService.MenuOrder.ToList();
        if (order.Count == 0) return;

        for (int targetIdx = 0; targetIdx < order.Count; targetIdx++)
        {
            var key = order[targetIdx];
            for (int i = targetIdx; i < TopMenu.Items.Count; i++)
            {
                if (TopMenu.Items[i] is System.Windows.Controls.MenuItem mi &&
                    (string?)mi.Tag == key)
                {
                    if (i != targetIdx)
                    {
                        TopMenu.Items.RemoveAt(i);
                        TopMenu.Items.Insert(targetIdx, mi);
                    }
                    break;
                }
            }
        }
    }

    // ── Win32 API：Windows 標準アニメーション有効化 ──────────
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int SW_MINIMIZE                     = 6;
    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    private const int WM_NCHITTEST                    = 0x0084;
    private const int WM_NCMOUSEMOVE                  = 0x00A0;
    private const int WM_NCLBUTTONDOWN                = 0x00A1;
    private const int WM_NCLBUTTONUP                  = 0x00A2;
    private const int WM_NCMOUSELEAVE                 = 0x02A2;
    private const int HTMAXBUTTON                     = 9;

    // スナップレイアウト用：BtnMaximize のホバー状態トラッキング
    private bool _maxBtnHovered = false;

    /// <summary>DWMトランジションを有効化し、スナップレイアウト対応の WndProc フックを登録する。</summary>
    private void EnableNativeAnimations()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // DWM トランジションを有効化
        int disabled = 0;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED,
                              ref disabled, Marshal.SizeOf<int>());

        // WM_NCHITTEST フックを登録
        // → 最大化ボタン領域で HTMAXBUTTON を返すことで
        //   ホバー時に Windows 11 のスナップレイアウトが表示される
        HwndSource.FromHwnd(hwnd)?.AddHook(HwndHook);
    }

    // WinCtrlBtn スタイルは IsMouseOver / IsPressed トリガーベースのため
    // VisualStateManager.GoToState は効かない。
    // テンプレート内の "Bd" Border を Template.FindName で直接操作してホバー色を再現する。
    /// <summary>最大化ボタンのテンプレート内 Border の背景色をホバー状態に応じて直接変更する。</summary>
    private void SetMaxBtnBackground(string state)
    {
        try
        {
            if (BtnMaximize?.Template == null) return;
            BtnMaximize.ApplyTemplate();
            if (BtnMaximize.Template.FindName("Bd", BtnMaximize) is not Border bd) return;
            bd.Background = state switch
            {
                "MouseOver" => (System.Windows.Media.Brush)FindResource("BgHoverBrush"),
                "Pressed"   => (System.Windows.Media.Brush)FindResource("BorderBrush"),
                _           => System.Windows.Media.Brushes.Transparent,
            };
        }
        catch { /* リソース未解決時は無視 */ }
    }

    /// <summary>WM_NCHITTEST など最大化ボタン関連ウィンドウメッセージを処理してスナップレイアウトとアニメーションを制御する。</summary>
    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // ── WM_NCHITTEST ──
        // 最大化ボタン領域にいるとき HTMAXBUTTON を返す
        // → Windows 11 はこれを見てスナップレイアウトポップアップを表示する
        // 領域の入退を検知して BtnMaximize の背景色を手動更新する
        // （HTMAXBUTTON 返却中は WPF マウスイベントが NC 扱いになりトリガーが発火しないため）
        if (msg == WM_NCHITTEST && BtnMaximize?.IsVisible == true)
        {
            var screenPt = new System.Windows.Point(
                unchecked((short)(lParam.ToInt32() & 0xFFFF)),
                unchecked((short)((lParam.ToInt32() >> 16) & 0xFFFF)));
            try
            {
                var origin  = BtnMaximize.PointToScreen(new System.Windows.Point(0, 0));
                var btnRect = new Rect(origin,
                                       new Size(BtnMaximize.ActualWidth, BtnMaximize.ActualHeight));
                if (btnRect.Contains(screenPt))
                {
                    // 新規ホバーイン → ホバー色へ
                    if (!_maxBtnHovered)
                    {
                        _maxBtnHovered = true;
                        Dispatcher.BeginInvoke(() => SetMaxBtnBackground("MouseOver"));
                    }
                    handled = true;
                    return new IntPtr(HTMAXBUTTON);
                }
            }
            catch { /* ウィンドウ未表示時は無視 */ }

            // ボタン外に出た → 通常色へ
            if (_maxBtnHovered)
            {
                _maxBtnHovered = false;
                Dispatcher.BeginInvoke(() => SetMaxBtnBackground("Normal"));
            }
        }

        // ── WM_NCMOUSELEAVE ──
        // NC 領域全体からマウスが外れたときも通常色に戻す
        if (msg == WM_NCMOUSELEAVE && _maxBtnHovered)
        {
            _maxBtnHovered = false;
            Dispatcher.BeginInvoke(() => SetMaxBtnBackground("Normal"));
        }

        // ── WM_NCMOUSEMOVE (HTMAXBUTTON) ──
        // DefWindowProc に渡ると DWM がネイティブボタンの白い描画を行うため握りつぶす
        if (msg == WM_NCMOUSEMOVE && wParam.ToInt32() == HTMAXBUTTON)
        {
            handled = true;
            return IntPtr.Zero;
        }

        // ── WM_NCLBUTTONDOWN (HTMAXBUTTON) ──
        // DefWindowProc に渡すと OS が最大化処理を行うため抑制する
        if (msg == WM_NCLBUTTONDOWN && wParam.ToInt32() == HTMAXBUTTON)
        {
            Dispatcher.BeginInvoke(() => SetMaxBtnBackground("Pressed"));
            handled = true;
            return IntPtr.Zero;
        }

        // ── WM_NCLBUTTONUP (HTMAXBUTTON) ──
        // マウスボタンが離れたタイミングで最大化 / 復元を自前でトグルする
        if (msg == WM_NCLBUTTONUP && wParam.ToInt32() == HTMAXBUTTON)
        {
            Dispatcher.BeginInvoke(() =>
            {
                SetMaxBtnBackground("MouseOver");
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            });
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    // ── ウィンドウ操作 ─────────────────────────────────────
    /// <summary>DWM フラッシュ後にバックグラウンドスレッドで最小化アニメーションを確実に発火させる。</summary>
    private void WinMinimize_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // バックグラウンドスレッドで実行することで WPF Dispatcher の優先度問題を切り離す。
        // DwmFlush × 2:
        //   1回目 → DWM が現在合成中のフレームを完了させる
        //   2回目 → WPF が送り込んだ最新フレームを DWM が取り込んで完了させる
        // これで「DWM が最新のウィンドウ描画を把握した状態」で ShowWindow が呼ばれ
        // 吸い込みアニメーションが毎回確実に発火する。
        Task.Run(() =>
        {
            DwmFlush();
            DwmFlush();
            ShowWindow(hwnd, SW_MINIMIZE);
        });
    }

    private void WinMaximize_Click(object sender, System.Windows.RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void WinClose_Click(object sender, System.Windows.RoutedEventArgs e)
        => Close();

    /// <summary>ウィンドウ状態変化時に最大化アイコンを切り替え、最大化マージンを補正する。</summary>
    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (BtnMaximize == null || MaximizeIcon == null) return;
        MaximizeIcon.Data = (System.Windows.Media.Geometry)FindResource(
            WindowState == WindowState.Maximized ? "Bi.FullscreenExit" : "Bi.Square");

        // 最大化時、Windows はリサイズ境界分だけウィンドウをスクリーン外に配置するため
        // タイトルバー等の内容が上にずれて見える。その分を RootGrid のマージンで補正する。
        RootGrid.Margin = WindowState == WindowState.Maximized
            ? new Thickness(
                SystemParameters.WindowResizeBorderThickness.Left,
                SystemParameters.WindowResizeBorderThickness.Top,
                SystemParameters.WindowResizeBorderThickness.Right,
                SystemParameters.WindowResizeBorderThickness.Bottom)
            : new Thickness(0);
    }

    /// <summary>WidgetServiceProvider を構築して BookmarkWidget インスタンスを初期化する。</summary>
    private void InitBookmarkWidget()
    {
        var svc = new TKer.Services.WidgetServiceProvider(
            _vm.AppSettingsService,
            _vm.ScheduleService,
            _vm.ProjectService,
            _vm.TodoService,
            view =>
            {
                _vm.NavigateToCommand.Execute(view);
                Application.Current.MainWindow?.Activate();
            });
        _bookmarkWidget = new BookmarkWidget(svc);
    }

    /// <summary>栞ウィジェットの表示・非表示をトグルする。</summary>
    private void MenuBookmarkWidget_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_bookmarkWidget == null) InitBookmarkWidget();

        if (_bookmarkWidget!.IsVisible)
            _bookmarkWidget.Hide();
        else
            _bookmarkWidget.ShowWidget();
    }

    /// <summary>ウィジェット専用プロセスを --widget 引数付きで起動する。</summary>
    private void MenuLaunchWidgetProcess_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath))
            {
                MessageBox.Show("実行ファイルのパスを取得できませんでした。");
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = exePath,
                Arguments       = "--widget",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ウィジェットプロセスの起動に失敗しました:\n{ex.Message}");
        }
    }

    /// <summary>ウィンドウを閉じる前にプロジェクトを保存しウィジェットを強制終了する。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_vm.IsProjectLoaded)
            _vm.ProjectService.SaveProject();

        try { _bookmarkWidget?.ForceClose(); } catch { }
    }
}

/// <summary>ページが画面遷移時に最新データを再読み込みするためのインターフェース。</summary>
public interface IRefreshable { void Refresh(); }

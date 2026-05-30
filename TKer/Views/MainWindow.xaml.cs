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

    // トップメニューから遷移直後に実行する保留アクション（"add"）
    private string? _pendingPageAction;

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

        // 再生中メディア（システム）の取得開始
        InitMediaSession();
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
                    // ローディングオーバーレイを消し切ってから保留アクション（モーダル
                    // ダイアログ等）を適用する。表示中に適用するとローディング画面が
                    // 固まって見えるため。
                    HideLoadingOverlay(() =>
                    {
                        if (_pendingPageAction is { } action)
                        {
                            ApplyPageAction(page, action);
                            _pendingPageAction = null;
                        }
                    });
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
        "CollectionItems"     => new CollectionItemsPage(_vm),
        "UiCustomize"         => new UiCustomizePage(_vm),
        "Pomodoro"            => new PomodoroPage(_vm),
        "Article"             => new ArticlePage(_vm),

        "LogViewer"           => new LogViewerPage(_vm),
        "Todo"                => new TodoPage(_vm),
        "Setup"               => new ProjectListPage(_vm),
        "WindowLayout"        => new WindowLayoutPage(_vm),
        _                     => new HomePage(_vm),
    };

    /// <summary>ローディングオーバーレイをフェードアウトアニメーションで非表示にする。</summary>
    private void HideLoadingOverlay(Action? onComplete = null)
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) =>
        {
            LoadingOverlay.BeginAnimation(OpacityProperty, null);
            LoadingOverlay.Visibility = Visibility.Collapsed;
            onComplete?.Invoke();
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

    // ── ライブラリ：プロジェクト ────────────────────────────

    /// <summary>プロジェクト一覧へ遷移し、追加フォームを開いた状態にする。</summary>
    private void ProjectAdd_Click(object sender, System.Windows.RoutedEventArgs e)
        => RunLibraryAction("ProjectList", "add");

    /// <summary>読み込みダイアログをそのまま表示し、ファイル選択時にプロジェクトを切り替える（切替後に画面遷移）。</summary>
    private void ProjectLoad_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "プロジェクトファイルを選択",
            Filter = "プロジェクトファイル|*_project.json|すべてのファイル|*.*"
        };
        if (dlg.ShowDialog() == true)
            _vm.SwitchProjectCommand.Execute(dlg.FileName);
    }

    // ── ライブラリ：コレクション ────────────────────────────

    /// <summary>コレクション画面へ遷移し、追加フォームを開いた状態にする。</summary>
    private void CollectionAdd_Click(object sender, System.Windows.RoutedEventArgs e)
        => RunLibraryAction("Collection", "add");

    /// <summary>読み込みダイアログをそのまま表示し、選択時にコレクションを取り込んで画面遷移・更新する。</summary>
    private void CollectionLoad_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "コレクションファイルを選択",
            Filter = "コレクションファイル|*_collection.json|すべてのファイル|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var col  = Newtonsoft.Json.JsonConvert.DeserializeObject<TKer.Models.Collection>(json);
            if (col == null)
            {
                Views.Dialogs.AppDialog.ShowError("ファイルの読み込みに失敗しました", "エラー", this);
                return;
            }
            if (_vm.CollectionService.Collections.Any(c => c.Id == col.Id))
            {
                Views.Dialogs.AppDialog.ShowWarning("このコレクションはすでに読み込まれています", "確認", this);
                return;
            }
            _vm.CollectionService.AddImported(col, dlg.FileName);
        }
        catch
        {
            Views.Dialogs.AppDialog.ShowError("ファイルの読み込みに失敗しました", "エラー", this);
            return;
        }

        // 選択された場合のみ画面遷移・更新する
        if (_vm.CurrentView == "Collection" && MainFrame.Content is CollectionPage cp)
            cp.Refresh();
        else
            _vm.NavigateToCommand.Execute("Collection");
    }

    /// <summary>
    /// 対象ビューへ遷移してアクションを適用する。すでに対象ビューを表示中の場合は
    /// 遷移せず現在のページへ直接アクションを適用する（CurrentView 不変で遷移が起きないため）。
    /// </summary>
    private void RunLibraryAction(string view, string action)
    {
        if (_vm.CurrentView == view && MainFrame.Content is Page current)
        {
            ApplyPageAction(current, action);
            return;
        }
        _pendingPageAction = action;
        _vm.NavigateToCommand.Execute(view);
    }

    /// <summary>ページ種別に応じて保留アクション（"add"）を適用する。</summary>
    private void ApplyPageAction(Page page, string action)
    {
        switch (page)
        {
            case ProjectListPage plp when action == "add":
                plp.RequestShowAddPanel();
                break;
            case CollectionPage cpAdd when action == "add":
                cpAdd.RequestShowAddForm();
                break;
        }
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex,
        IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

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
        _mediaTimer?.Stop();
        _popupTimer?.Stop();
    }

    // ── 再生中メディア（システム）の取得・表示 ─────────────────
    // Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager で
    // OS が把握する「現在再生中」のセッション（Spotify, ブラウザ等）から
    // タイトル・アーティスト・サムネイルを取得し、サムネイルの主要色を背景に、
    // 白文字で曲名を表示する。再生中はタイトルを右→左へマーキー表示する。

    private global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _smtcManager;
    private DispatcherTimer? _mediaTimer;
    private DispatcherTimer? _popupTimer;
    private string? _lastMediaTitle;
    private string? _lastMediaAumid;
    private bool _marqueeRunning;
    private bool _popupAnimating;

    /// <summary>SMTC セッションマネージャーを初期化し、ポーリングタイマーを開始する。</summary>
    private async void InitMediaSession()
    {
        try
        {
            _smtcManager = await global::Windows.Media.Control
                .GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch
        {
            // WinRT が利用できない環境では機能を無効化（パネルは非表示のまま）
            return;
        }

        _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _mediaTimer.Tick += async (_, _) => await RefreshNowPlaying();
        _mediaTimer.Start();

        // ポップアップ外クリック／ウィンドウ非アクティブで閉じる
        PreviewMouseDown += Window_PreviewMouseDownForPopup;
        Deactivated += (_, _) => { if (MediaPopup.IsOpen && !_popupAnimating) ClosePopupAnimated(); };

        // ウィンドウ移動・リサイズ時にポップアップ位置を追従させる
        LocationChanged += (_, _) => ForcePopupReposition();
        SizeChanged     += (_, _) => ForcePopupReposition();

        await RefreshNowPlaying();
    }

    /// <summary>現在のセッションからタイトル・サムネイルを取得して表示を更新する。</summary>
    private async Task RefreshNowPlaying()
    {
        var session = _smtcManager?.GetCurrentSession();
        if (session == null) { HideMedia(); return; }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var info  = session.GetPlaybackInfo();

            var title  = props?.Title  ?? "";
            var artist = props?.Artist ?? "";
            if (string.IsNullOrWhiteSpace(title)) { HideMedia(); return; }

            var display = string.IsNullOrWhiteSpace(artist) ? title : $"{title} — {artist}";
            bool playing = info?.PlaybackStatus
                == global::Windows.Media.Control
                    .GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // ポップアップ表示中は欄を隠したままにする（変形演出のため）
            if (!MediaPopup.IsOpen) MediaPanel.Visibility = Visibility.Visible;

            // 再生アプリが変わったらアイコンを更新
            var aumid = session.SourceAppUserModelId ?? "";
            if (aumid != _lastMediaAumid)
            {
                _lastMediaAumid = aumid;
                _ = UpdateAppIcon(aumid);
            }

            if (display != _lastMediaTitle)
            {
                // 曲が変わったとき：テキスト・背景色・マーキーを作り直す
                _lastMediaTitle    = display;
                MediaTrackName.Text = display;
                PopupTitle.Text     = title;
                PopupSubtitle.Text  = artist;
                _ = UpdateBackgroundFromThumbnail(props);
                SetupMarquee(playing);
            }
            else
            {
                // 同じ曲：再生状態に応じてマーキーの開始/停止のみ同期
                if (playing && !_marqueeRunning)      SetupMarquee(true);
                else if (!playing && _marqueeRunning) StopMarquee();
            }

            // ポップアップ表示中はタイムライン・再生状態を更新
            if (MediaPopup.IsOpen) UpdatePopupTimeline(session, playing);
        }
        catch
        {
            HideMedia();
        }
    }

    /// <summary>メディア表示を隠し、マーキーを停止する。</summary>
    private void HideMedia()
    {
        MediaPanel.Visibility = Visibility.Collapsed;
        _lastMediaTitle = null;
        _lastMediaAumid = null;
        StopMarquee();
        if (MediaPopup.IsOpen) { _popupAnimating = false; MediaPopup.IsOpen = false; }
    }

    /// <summary>再生中アプリのアイコンを取得し、白シルエットで表示する。</summary>
    private async Task UpdateAppIcon(string aumid)
    {
        // ① パッケージアプリ（Store 版など）：AppInfo からロゴ取得
        var src = await TryGetPackagedAppLogo(aumid);

        // ② Win32 アプリ（Spotify デスクトップ等）：実行ファイルからアイコン抽出
        src ??= TryGetWin32AppIcon(aumid);

        if (src != null)
        {
            AppIconImage.Source        = src;
            AppIconImage.Visibility    = Visibility.Visible;
            AppIconFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            // ③ いずれも不可：白音符アイコンにフォールバック
            AppIconImage.Source        = null;
            AppIconImage.Visibility    = Visibility.Collapsed;
            AppIconFallback.Visibility = Visibility.Visible;
        }

        PopupAppIcon.Source = src; // ポップアップのタイトル左上アイコン
    }

    /// <summary>パッケージアプリのロゴを AppInfo 経由で取得する（失敗時は null）。</summary>
    private static async Task<BitmapSource?> TryGetPackagedAppLogo(string aumid)
    {
        try
        {
            if (string.IsNullOrEmpty(aumid)) return null;

            var appInfo = global::Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(aumid);
            var logoRef = appInfo.DisplayInfo.GetLogo(new global::Windows.Foundation.Size(32, 32));

            using var ras = await logoRef.OpenReadAsync();
            using var net = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await net.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Win32 アプリの実行ファイルパスを解決し、アイコンを抽出する（失敗時は null）。</summary>
    private static BitmapSource? TryGetWin32AppIcon(string aumid)
    {
        try
        {
            var exePath = ResolveExecutablePath(aumid);
            if (exePath == null) return null;

            var large = new IntPtr[1];
            uint extracted = ExtractIconEx(exePath, 0, large, null, 1);
            if (extracted == 0 || large[0] == IntPtr.Zero) return null;

            try
            {
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    large[0], System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DestroyIcon(large[0]);
            }
        }
        catch { return null; }
    }

    /// <summary>AppUserModelId（exe 名・パス）から実行ファイルのフルパスを解決する。</summary>
    private static string? ResolveExecutablePath(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return null;

        // すでにフルパスならそのまま
        if (aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(aumid))
            return aumid;

        // "Spotify.exe" や AUMID から実行プロセス名を推定して MainModule を引く
        var name = Path.GetFileNameWithoutExtension(aumid);
        if (string.IsNullOrEmpty(name)) return null;

        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                }
                catch { /* アクセス不可プロセスはスキップ */ }
            }
        }
        catch { }

        return null;
    }

    /// <summary>マーキー（右→左ループ）を設定する。再生中のみ流れ、停止中は左寄せ静止。</summary>
    private void SetupMarquee(bool playing)
    {
        MarqueeTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        _marqueeRunning = false;

        MediaTrackName.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textW = MediaTrackName.DesiredSize.Width;
        // マーキー領域（アイコン分を除いた幅）。レイアウト前は実幅が 0 なので固定値で補う
        double viewW = MarqueeCanvas.ActualWidth > 0 ? MarqueeCanvas.ActualWidth : 174;

        if (!playing)
        {
            MarqueeTransform.X = 4;
            return;
        }

        // 右端外（X=viewW）から左端外（X=-textW）へ一定速度で流し、無限ループ
        double start    = viewW;
        double end      = -textW;
        double distance = start - end;
        const double speed = 45; // px/sec
        var anim = new DoubleAnimation(start, end, TimeSpan.FromSeconds(distance / speed))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        MarqueeTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
        _marqueeRunning = true;
    }

    /// <summary>マーキーを停止して左寄せ静止にする。</summary>
    private void StopMarquee()
    {
        MarqueeTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        MarqueeTransform.X = 4;
        _marqueeRunning = false;
    }

    // ── Now Playing ポップアップ ─────────────────────────────

    /// <summary>メディアパネルのクリックでポップアップを開閉する（開＝拡大 / 閉＝縮小）。</summary>
    private async void MediaPanel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_popupAnimating) return;

        if (MediaPopup.IsOpen)
        {
            ClosePopupAnimated();
            return;
        }

        // 欄がポップアップに変形したように見せるため、表示中は欄を隠す
        // （Hidden でレイアウト幅は保持し、配置基準・プロジェクト名位置を維持）
        MediaPanel.Visibility = Visibility.Hidden;

        // コンテンツのみから高さを測り、ポップアップを実サイズで固定
        // （背景画像に高さを引っ張られないようにする）
        PopupContent.Measure(new Size(320, double.PositiveInfinity));
        double targetH = PopupContent.DesiredSize.Height;
        if (double.IsNaN(targetH) || targetH < 40) targetH = 150;
        PopupRoot.Width  = 320;
        PopupRoot.Height = targetH;

        // 前回の保持アニメーションをクリアしてから開始値を設定
        PopupScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        PopupScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        MediaPopup.BeginAnimation(System.Windows.Controls.Primitives.Popup.VerticalOffsetProperty, null);
        PopupContent.BeginAnimation(OpacityProperty, null);

        // 開始状態：メディア欄の位置・縦横比にぴったり重ねる
        // （VerticalOffset=-24 で欄の上端に合わせ、欄サイズに縮小 → 欄に見える）
        PopupScale.ScaleX    = MediaPanel.Width  / 320.0;
        PopupScale.ScaleY    = MediaPanel.Height / targetH;
        MediaPopup.VerticalOffset = -MediaPanel.Height;
        PopupContent.Opacity = 0;

        MediaPopup.IsOpen = true;
        AnimatePopupOpen();

        // 0.5秒ごとに経過時間バーを滑らかに更新
        _popupTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _popupTimer.Tick -= PopupTimer_Tick;
        _popupTimer.Tick += PopupTimer_Tick;
        _popupTimer.Start();
        await RefreshNowPlaying();
    }

    /// <summary>ポップアップ外クリックで縮小アニメーションして閉じる。</summary>
    private void Window_PreviewMouseDownForPopup(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!MediaPopup.IsOpen || _popupAnimating) return;
        var src = e.OriginalSource as DependencyObject;
        // ポップアップ内のクリックでは閉じない（シーク・ボタン操作を許可）
        if (IsWithin(src, PopupRoot)) return;
        // メディア欄上のクリックは MediaPanel_Click（トグル）に任せる
        if (IsWithin(src, MediaPanel)) return;
        ClosePopupAnimated();
    }

    /// <summary>指定要素が祖先 target の配下にあるか判定する。</summary>
    private static bool IsWithin(DependencyObject? node, DependencyObject target)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, target)) return true;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node)
                   ?? (node as FrameworkElement)?.Parent;
        }
        return false;
    }

    /// <summary>ポップアップをメディア欄の縦横比へスムーズに縮小しながら閉じる。</summary>
    private void ClosePopupAnimated()
    {
        if (!MediaPopup.IsOpen || _popupAnimating) return;
        _popupAnimating = true;
        _popupTimer?.Stop();

        double endX = MediaPanel.Width  / Math.Max(1, PopupRoot.Width);
        double endY = MediaPanel.Height / Math.Max(1, PopupRoot.Height);

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var dur  = TimeSpan.FromMilliseconds(320);

        var syAnim = new DoubleAnimation(PopupScale.ScaleY, endY, dur) { EasingFunction = ease };
        syAnim.Completed += (_, _) =>
        {
            MediaPopup.IsOpen = false;
            PopupScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            PopupScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            MediaPopup.BeginAnimation(System.Windows.Controls.Primitives.Popup.VerticalOffsetProperty, null);
            PopupScale.ScaleX = 1; PopupScale.ScaleY = 1;
            MediaPopup.VerticalOffset = 6;
            _popupAnimating = false;
        };

        // 欄の位置（上端）へ戻りながら欄サイズへ縮む
        PopupContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(PopupContent.Opacity, 0, TimeSpan.FromMilliseconds(160)));
        MediaPopup.BeginAnimation(System.Windows.Controls.Primitives.Popup.VerticalOffsetProperty,
            new DoubleAnimation(MediaPopup.VerticalOffset, -MediaPanel.Height, dur) { EasingFunction = ease });
        PopupScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(PopupScale.ScaleX, endX, dur) { EasingFunction = ease });
        PopupScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, syAnim);
    }

    /// <summary>メディア欄の位置・縦横比から下へ移動しながら等倍へ拡大して開く。</summary>
    private void AnimatePopupOpen()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur  = TimeSpan.FromMilliseconds(420);

        // 欄の上端(-Height)から最終位置(6)へ下りながら、欄サイズ→等倍へ拡大
        MediaPopup.BeginAnimation(System.Windows.Controls.Primitives.Popup.VerticalOffsetProperty,
            new DoubleAnimation(-MediaPanel.Height, 6, dur) { EasingFunction = ease });
        PopupScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(PopupScale.ScaleX, 1, dur) { EasingFunction = ease });
        PopupScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(PopupScale.ScaleY, 1, dur) { EasingFunction = ease });

        // 中身は移動・拡大の後半でフェードイン
        PopupContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(280)) { BeginTime = TimeSpan.FromMilliseconds(160) });
    }

    /// <summary>角丸でクリップするため、サイズ確定時に丸角矩形のクリップを設定する。</summary>
    private void ForcePopupReposition()
    {
        if (!MediaPopup.IsOpen) return;
        var offset = MediaPopup.HorizontalOffset;
        MediaPopup.HorizontalOffset = offset + 1;
        MediaPopup.HorizontalOffset = offset;
    }

    private void PopupRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double r = Math.Min(14, e.NewSize.Height / 2);
        PopupRoot.Clip = new System.Windows.Media.RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), r, r);
    }

    /// <summary>ポップアップが閉じたらタイマーを止め、メディア欄を再表示する。</summary>
    private void MediaPopup_Closed(object? sender, EventArgs e)
    {
        _popupTimer?.Stop();
        // 再生中（タイトルあり）のときのみ欄を戻す（停止時は Collapsed のまま）
        if (_lastMediaTitle != null) MediaPanel.Visibility = Visibility.Visible;
    }

    private async void PopupTimer_Tick(object? sender, EventArgs e)
    {
        if (!MediaPopup.IsOpen) { _popupTimer?.Stop(); return; }
        var session = _smtcManager?.GetCurrentSession();
        if (session == null) return;
        bool playing = session.GetPlaybackInfo()?.PlaybackStatus
            == global::Windows.Media.Control
                .GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        UpdatePopupTimeline(session, playing);
        await Task.CompletedTask;
    }

    /// <summary>セッションのタイムラインから経過バー・時間表示・再生アイコンを更新する。</summary>
    private void UpdatePopupTimeline(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession session,
        bool playing)
    {
        // 再生アイコン
        PopupPlayIcon.Data = (System.Windows.Media.Geometry)
            FindResource(playing ? "Bi.PauseFill" : "Bi.PlayFill");

        try
        {
            var tl = session.GetTimelineProperties();
            var duration = tl.EndTime - tl.StartTime;
            var pos      = tl.Position - tl.StartTime;

            // 再生中は最終更新からの経過を加算して滑らかに進める
            if (playing)
            {
                var elapsed = DateTimeOffset.Now - tl.LastUpdatedTime;
                if (elapsed > TimeSpan.Zero) pos += elapsed;
            }

            if (duration <= TimeSpan.Zero)
            {
                ProgressFill.Width    = 0;
                PopupCurTime.Text     = "0:00";
                PopupTotTime.Text     = "0:00";
                return;
            }

            if (pos < TimeSpan.Zero)     pos = TimeSpan.Zero;
            if (pos > duration)          pos = duration;

            double frac = pos.TotalSeconds / duration.TotalSeconds;
            ProgressFill.Width = Math.Max(0, ProgressTrack.ActualWidth * frac);
            PopupCurTime.Text  = FormatTime(pos);
            PopupTotTime.Text  = FormatTime(duration);
        }
        catch
        {
            ProgressFill.Width = 0;
        }
    }

    private static string FormatTime(TimeSpan t) =>
        $"{(int)t.TotalMinutes}:{t.Seconds:D2}";

    /// <summary>経過バーのクリック位置に応じてシークする。</summary>
    private async void ProgressTrack_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var session = _smtcManager?.GetCurrentSession();
        if (session == null) return;

        try
        {
            var tl = session.GetTimelineProperties();
            var duration = tl.EndTime - tl.StartTime;
            if (duration <= TimeSpan.Zero) return;

            double x    = e.GetPosition(ProgressTrack).X;
            double frac = Math.Clamp(x / ProgressTrack.ActualWidth, 0, 1);
            var target  = tl.StartTime + TimeSpan.FromTicks((long)(duration.Ticks * frac));

            await session.TryChangePlaybackPositionAsync(target.Ticks);
            UpdatePopupTimeline(session, true);
        }
        catch { }
    }

    /// <summary>再生 / 一時停止を切り替える。</summary>
    private async void MediaPlayPause_Click(object sender, RoutedEventArgs e)
    {
        var session = _smtcManager?.GetCurrentSession();
        if (session == null) return;
        try { await session.TryTogglePlayPauseAsync(); } catch { }
        await RefreshNowPlaying();
    }

    /// <summary>前のトラックへスキップする。</summary>
    private async void MediaPrev_Click(object sender, RoutedEventArgs e)
    {
        var session = _smtcManager?.GetCurrentSession();
        if (session == null) return;
        try { await session.TrySkipPreviousAsync(); } catch { }
        await RefreshNowPlaying();
    }

    /// <summary>次のトラックへスキップする。</summary>
    private async void MediaNext_Click(object sender, RoutedEventArgs e)
    {
        var session = _smtcManager?.GetCurrentSession();
        if (session == null) return;
        try { await session.TrySkipNextAsync(); } catch { }
        await RefreshNowPlaying();
    }

    /// <summary>サムネイルから主要色を抽出し、ぼかし風グラデーション背景を設定する。</summary>
    private async Task UpdateBackgroundFromThumbnail(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties? props)
    {
        try
        {
            var thumbRef = props?.Thumbnail;
            if (thumbRef == null)
            {
                SetMediaBackground(System.Windows.Media.Color.FromRgb(45, 45, 48));
                PopupBgImage.Source = null;
                return;
            }

            using var ras = await thumbRef.OpenReadAsync();
            using var net = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await net.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            SetMediaBackground(GetDominantColor(bmp));
            PopupBgImage.Source = bmp; // ポップアップ背景（ぼかしはXAML側のBlurEffect）
        }
        catch
        {
            SetMediaBackground(System.Windows.Media.Color.FromRgb(45, 45, 48));
        }
    }

    /// <summary>画像を縮小し、最も出現比率の高い色（量子化バケットの代表色）を返す。</summary>
    private static System.Windows.Media.Color GetDominantColor(BitmapSource src)
    {
        const int w = 16, h = 16;
        var scaled = new TransformedBitmap(src,
            new System.Windows.Media.ScaleTransform((double)w / src.PixelWidth, (double)h / src.PixelHeight));
        var conv = new FormatConvertedBitmap(scaled, System.Windows.Media.PixelFormats.Bgra32, null, 0);

        int stride = w * 4;
        var pixels = new byte[h * stride];
        conv.CopyPixels(pixels, stride, 0);

        // 量子化バケットごとに出現数と実色の合計を集計
        var count = new Dictionary<int, int>();
        var rSum  = new Dictionary<int, long>();
        var gSum  = new Dictionary<int, long>();
        var bSum  = new Dictionary<int, long>();

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
            if (a < 128) continue;
            int key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
            count.TryGetValue(key, out int c);
            count[key] = c + 1;
            rSum[key] = (rSum.TryGetValue(key, out long rs) ? rs : 0) + r;
            gSum[key] = (gSum.TryGetValue(key, out long gs) ? gs : 0) + g;
            bSum[key] = (bSum.TryGetValue(key, out long bs) ? bs : 0) + b;
        }

        if (count.Count == 0) return System.Windows.Media.Color.FromRgb(45, 45, 48);

        int best = count.OrderByDescending(kv => kv.Value).First().Key;
        int n = count[best];
        return System.Windows.Media.Color.FromRgb(
            (byte)(rSum[best] / n), (byte)(gSum[best] / n), (byte)(bSum[best] / n));
    }

    /// <summary>主要色から左右が暗いぼかし風の横グラデーション背景を設定する。</summary>
    private void SetMediaBackground(System.Windows.Media.Color c)
    {
        System.Windows.Media.Color Mul(double f) => System.Windows.Media.Color.FromRgb(
            (byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

        var dark = Mul(0.5);
        var brush = new System.Windows.Media.LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint   = new Point(1, 0),
        };
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(dark, 0));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(c,    0.5));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(dark, 1));
        brush.Freeze();
        MediaPanel.Background = brush;
    }
}

/// <summary>ページが画面遷移時に最新データを再読み込みするためのインターフェース。</summary>
public interface IRefreshable { void Refresh(); }

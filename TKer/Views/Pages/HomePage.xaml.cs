using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>プロジェクト概要・アラート・ショートカットなどを集約したホーム画面ページ。</summary>
public partial class HomePage : Page, IRefreshable
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _clockTimer;
    private DateTime _calMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    /// <summary>編集プレビュー（カスタマイズ画面の仮想ウィンドウ）として表示中か。
    /// true のときショートカットラインに「＋（追加）」ノードを表示する。</summary>
    public bool IsEditPreview { get; set; } = false;

    /// <summary>右列の部品が「クリック」されたとき（ドラッグでなかった場合）に通知するコールバック。
    /// カスタマイズ画面がスタイル編集対象として選択するのに使う。</summary>
    public Action<string>? RightWidgetClicked { get; set; }

    /// <summary>ホームページを初期化し、時計タイマーを起動する。</summary>
    public HomePage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            var now = DateTime.Now.ToString("HH:mm:ss");
            if (ClockText != null) ClockText.Text = now;
            if (PlanetClockText != null) PlanetClockText.Text = now;
        };
        _clockTimer.Start();

        Unloaded += (_, _) => { _clockTimer.Stop(); StopCardAnimation(); StopCardMedia(); };
        Loaded   += (_, _) => _clockTimer.Start();

        // 左列の部品ドラッグ並べ替え（編集プレビュー時のみ動作）
        CardLeftStack.PreviewMouseLeftButtonDown += LeftStack_Down;
        CardLeftStack.PreviewMouseMove          += LeftStack_Move;
        CardLeftStack.PreviewMouseLeftButtonUp  += LeftStack_Up;
    }

    /// <summary>ホーム画面の全コンポーネントを最新データで更新する。</summary>
    public void Refresh()
    {
        // ── テンプレート分岐：専用ビューを表示して終了 ──
        var template = _vm.AppSettingsService.HomeTemplate;
        if (template == "Planet")
        {
            HomeScroll.Visibility = Visibility.Collapsed;
            CardView.Visibility   = Visibility.Collapsed;
            PlanetView.Visibility = Visibility.Visible;
            RefreshPlanetView();
            return;
        }
        // Card と「未実装テンプレート（Magazine/Dock/Tri/Timeline/CalendarFull/Journal/Glass）」は
        // とりあえずカードビューを表示しておく。実装時に分岐を追加する。
        if (template is "Card" or "Magazine" or "Dock" or "Tri"
                     or "Timeline" or "CalendarFull" or "Journal" or "Glass")
        {
            HomeScroll.Visibility = Visibility.Collapsed;
            PlanetView.Visibility = Visibility.Collapsed;
            CardView.Visibility   = Visibility.Visible;
            RefreshCardView();
            return;
        }
        HomeScroll.Visibility = Visibility.Visible;
        PlanetView.Visibility = Visibility.Collapsed;
        CardView.Visibility   = Visibility.Collapsed;

        // ── プロジェクト名・日時 ──────────────────────────────
        ProjectTitleText.Text = _vm.IsProjectLoaded
            ? _vm.ProjectTitle
            : "TKer";
        DateText.Text = DateTime.Now.ToString("yyyy年MM月dd日 (ddd)");
        ClockText.Text = DateTime.Now.ToString("HH:mm:ss");

        // ── バージョン ───────────────────────────────
        VersionText.Text = AppVersion.DISPLAY_NAME;
        BuildText.Text   = $"Build {AppVersion.BUILD_DATE}";
        InfoVersion.Text = AppVersion.CURRENT;
        InfoBuild.Text   = AppVersion.BUILD_DATE;

        // ── アラート ─────────────────────────────────
        var alerts = _vm.AppSettingsService.CollectAlerts();
        _vm.AlertCount = alerts.Count;

        if (alerts.Count == 0)
        {
            AlertBadge.Visibility    = Visibility.Collapsed;
            NoAlertBanner.Visibility = Visibility.Visible;
            AlertList.ItemsSource    = null;
        }
        else
        {
            AlertBadge.Visibility    = Visibility.Visible;
            AlertCountText.Text      = alerts.Count.ToString();
            NoAlertBanner.Visibility = Visibility.Collapsed;
            AlertList.ItemsSource    = alerts.Take(8).ToList(); // 最大8件
        }

        // ── プロジェクト一覧 ──────────────────────────
        var summaries = _vm.AppSettingsService.CollectSummaries();
        if (summaries.Count == 0)
        {
            NoProjectBanner.Visibility = Visibility.Visible;
            ProjectList.ItemsSource    = null;
        }
        else
        {
            NoProjectBanner.Visibility = Visibility.Collapsed;
            ProjectList.ItemsSource    = summaries;
        }

        BuildRecentTasks();
        BuildShortcuts();
        BuildMiniCalendar();
        ApplyLayout();
        ApplySectionThemes();

        // ── クイックナビ: プロジェクト未ロード時は薄く ──
        var loaded = _vm.IsProjectLoaded;
        BtnNavDashboard.IsEnabled = loaded;
        BtnNavTask.IsEnabled      = loaded;
        BtnNavCalendar.IsEnabled  = loaded;
        BtnNavDashboard.Opacity   = loaded ? 1.0 : 0.4;
        BtnNavTask.Opacity        = loaded ? 1.0 : 0.4;
        BtnNavCalendar.Opacity    = loaded ? 1.0 : 0.4;
    }

    // ── プロジェクトカードクリック → 切替 ───────────
    /// <summary>プロジェクトカードクリック時に対象プロジェクトへ切り替える。</summary>
    private void ProjectCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // ボタンへのバブリングは止める
        if (e.OriginalSource is Button) return;
        if (((Border)sender).DataContext is not ProjectSummary summary) return;
        _vm.SwitchProjectCommand.Execute(summary.Entry.DataFilePath);
    }

    // ── アラートクリック → 該当プロジェクトを開いてタスク一覧 ──
    /// <summary>アラートクリック時に対象プロジェクトを開いてタスク一覧へ遷移する。</summary>
    private void AlertItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (((Border)sender).DataContext is not AlertItem alert) return;
        _vm.SwitchProjectCommand.Execute(alert.DataFilePath);
        _vm.NavigateToCommand.Execute("TaskList");
    }

    // ── ピン留め ─────────────────────────────────────
    /// <summary>プロジェクトのピン留め状態を切り替える。</summary>
    private void PinProject_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is string path)
        {
            _vm.AppSettingsService.TogglePin(path);
            Refresh();
        }
    }

    // ── 一覧から削除 ─────────────────────────────────
    /// <summary>確認ダイアログ後にプロジェクトを一覧から削除する。</summary>
    private void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string path) return;
        if (AppDialog.Confirm("一覧からこのプロジェクトを削除しますか？\n（プロジェクトフォルダは削除されません）", "確認", Window.GetWindow(this)))
        {
            _vm.AppSettingsService.RemoveProject(path);
            Refresh();
        }
    }

    // ── 新規/開く ────────────────────────────────────
    private void NewProject_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("ProjectList");

    // ── すべてのアラートを表示 ───────────────────────
    /// <summary>アラート一覧ダイアログを表示する。</summary>
    private void ShowAllAlerts_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AlertListDialog(_vm.AppSettingsService, _vm) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
        Refresh();
    }

    // ── クイックナビ ─────────────────────────────────
    /// <summary>クイックナビボタンのTag値が示すビューへ遷移する。</summary>
    private void QuickNav_Click(object sender, RoutedEventArgs e)
    {
        var view = ((Button)sender).Tag as string;
        if (!string.IsNullOrEmpty(view))
            _vm.NavigateToCommand.Execute(view);
    }

    // ── 直近タスク ───────────────────────────────────
    /// <summary>最近更新されたタスク上位5件を直近タスクリストに表示する。</summary>
    private void BuildRecentTasks()
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null || project.Tasks.Count == 0)
        {
            NoRecentTaskBanner.Visibility = Visibility.Visible;
            RecentTaskList.ItemsSource    = null;
            return;
        }
        NoRecentTaskBanner.Visibility = Visibility.Collapsed;
        var catMap = project.Categories.ToDictionary(c => c.Id, c => c.Name);
        RecentTaskList.ItemsSource = project.Tasks
            .OrderByDescending(t => t.UpdatedAt)
            .Take(5)
            .Select(t => new
            {
                t.Name, t.Assignee, t.Priority, t.Status,
                t.PlannedEndDate,
                CategoryName = catMap.GetValueOrDefault(t.CategoryId, ""),
                Source = t
            })
            .ToList();
    }

    private void RecentTask_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _vm.NavigateToCommand.Execute("TaskList");
    }

    private void GoToTaskList_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("TaskList");

    private void GoToProjectList_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("ProjectList");

    // ── ショートカット ───────────────────────────────
    /// <summary>登録済みショートカットのボタン一覧を構築する。</summary>
    private void BuildShortcuts()
    {
        ShortcutPanel.Children.Clear();
        var shortcuts = _vm.AppSettingsService.Shortcuts;
        if (shortcuts.Count == 0)
        {
            NoShortcutText.Visibility = Visibility.Visible;
            return;
        }
        NoShortcutText.Visibility = Visibility.Collapsed;
        foreach (var sc in shortcuts)
        {
            var path = sc.Path;
            var btn = new Button
            {
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 6, 10, 6),
                Cursor  = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(47, 47, 47)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                BorderThickness = new Thickness(1),
                ToolTip = path
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = sc.Icon, FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = sc.Name, FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(207, 207, 207)),
                VerticalAlignment = VerticalAlignment.Center
            });
            btn.Content = sp;
            btn.Click += (_, _) => OpenShortcut(path);
            ShortcutPanel.Children.Add(btn);
        }
    }

    /// <summary>ホーム画面の各セクションにテーマを適用する。</summary>
    private void ApplySectionThemes()
    {
        var svc = _vm.AppSettingsService;
        UiThemeHelper.ApplySectionTheme(CardHeader,        svc.GetSectionTheme("Header"));
        UiThemeHelper.ApplySectionTheme(CardAlert,         svc.GetSectionTheme("Alert"));
        UiThemeHelper.ApplySectionTheme(RecentTaskSection, svc.GetSectionTheme("RecentTask"));
        UiThemeHelper.ApplySectionTheme(CardProject,       svc.GetSectionTheme("Project"));
        UiThemeHelper.ApplySectionTheme(ShortcutCard,      svc.GetSectionTheme("Shortcut"));
        UiThemeHelper.ApplySectionTheme(CardCalendar,      svc.GetSectionTheme("Calendar"));
        UiThemeHelper.ApplySectionTheme(CardQuickNav,      svc.GetSectionTheme("QuickNav"));
        UiThemeHelper.ApplySectionTheme(CardVersion,       svc.GetSectionTheme("Version"));
    }

    // ── ホームレイアウト動的適用 ─────────────────────
    /// <summary>設定に基づいてホームグリッドのレイアウトを動的に構築する。</summary>
    private void ApplyLayout()
    {
        var map = new System.Collections.Generic.Dictionary<string, FrameworkElement>
        {
            ["Header"]     = CardHeader,
            ["Alert"]      = CardAlert,
            ["RecentTask"] = RecentTaskSection,
            ["Project"]    = CardProject,
            ["Shortcut"]   = ShortcutCard,
            ["Calendar"]   = CardCalendar,
            ["QuickNav"]   = CardQuickNav,
            ["Version"]    = CardVersion,
        };

        // 既存の親から切り離し
        foreach (var el in map.Values)
        {
            if (el.Parent is Panel p)
                p.Children.Remove(el);
        }
        HomeGrid.Children.Clear();
        HomeGrid.ColumnDefinitions.Clear();
        HomeGrid.RowDefinitions.Clear();

        // 列・行定義を構築
        var cols = _vm.AppSettingsService.GetEffectiveColumnWidths();
        var rows = _vm.AppSettingsService.GetEffectiveRowHeights();
        if (cols.Count == 0) cols.Add(-1);
        if (rows.Count == 0) rows.Add(-1);
        foreach (var w in cols)
            HomeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = ToGridLength(w) });
        foreach (var h in rows)
            HomeGrid.RowDefinitions.Add(new RowDefinition { Height = ToGridLength(h) });

        // 部品配置
        var slots = _vm.AppSettingsService.GetEffectiveHomeLayout();
        foreach (var slot in slots)
        {
            if (!map.TryGetValue(slot.ComponentId, out var el)) continue;
            el.Visibility = slot.Visible ? Visibility.Visible : Visibility.Collapsed;
            if (!slot.Visible) continue;
            el.Margin = new Thickness(4);
            el.Height    = double.NaN;
            el.MinHeight = 0;
            el.Width     = double.NaN;
            Grid.SetRow(el,        Math.Clamp(slot.Row,    0, Math.Max(0, rows.Count - 1)));
            Grid.SetColumn(el,     Math.Clamp(slot.Column, 0, Math.Max(0, cols.Count - 1)));
            Grid.SetRowSpan(el,    Math.Max(1, slot.RowSpan));
            Grid.SetColumnSpan(el, Math.Max(1, slot.ColumnSpan));
            HomeGrid.Children.Add(el);
        }
    }

    /// <summary>数値をGridLengthに変換する（0=Auto、負=Star、正=Pixel）。</summary>
    private static GridLength ToGridLength(double v)
    {
        if (v == 0) return GridLength.Auto;
        if (v < 0)  return new GridLength(-v, GridUnitType.Star);
        return new GridLength(v, GridUnitType.Pixel);
    }

    /// <summary>指定パスのファイル・アプリをシェルで開く。</summary>
    private static void OpenShortcut(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialog.ShowWarning($"開けませんでした:\n{ex.Message}", "エラー", null);
        }
    }

    private void ManageShortcuts_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("Shortcuts");

    // ── ミニカレンダー ───────────────────────────────
    private void CalPrev_Click(object sender, RoutedEventArgs e)
    {
        _calMonth = _calMonth.AddMonths(-1);
        BuildMiniCalendar();
    }

    private void CalNext_Click(object sender, RoutedEventArgs e)
    {
        _calMonth = _calMonth.AddMonths(1);
        BuildMiniCalendar();
    }

    /// <summary>ミニカレンダーを現在の表示月で再構築する。</summary>
    private void BuildMiniCalendar()
    {
        CalMonthLabel.Text = _calMonth.ToString("yyyy年 M月");
        MiniCalPanel.Children.Clear();

        var rawTasks = _vm.ProjectService.CurrentProject?.Tasks;
        var tasks = rawTasks != null ? rawTasks.ToList() : new System.Collections.Generic.List<TKer.Models.TaskItem>();
        var fg     = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(207, 207, 207));
        var dimFg  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(72, 72, 72));
        var todayBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 131, 226));
        var taskDot = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 158, 114));

        // Day headers (Sun=0 ... Sat=6, show Mon first)
        string[] headers = { "月", "火", "水", "木", "金", "土", "日" };
        var headerGrid = new System.Windows.Controls.Grid();
        for (int i = 0; i < 7; i++)
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 7; i++)
        {
            var tb = new System.Windows.Controls.TextBlock
            {
                Text = headers[i], TextAlignment = System.Windows.TextAlignment.Center,
                FontSize = 10, Margin = new Thickness(0, 0, 0, 4),
                Foreground = (i == 5) ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 120, 180))
                           : (i == 6) ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 80, 80))
                           : dimFg
            };
            System.Windows.Controls.Grid.SetColumn(tb, i);
            headerGrid.Children.Add(tb);
        }
        MiniCalPanel.Children.Add(headerGrid);

        // Calendar grid (Mon-based)
        var calGrid = new System.Windows.Controls.Grid();
        for (int i = 0; i < 7; i++) calGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < 6; r++) calGrid.RowDefinitions.Add(new RowDefinition());

        int firstDow = (int)_calMonth.DayOfWeek; // 0=Sun
        int startOffset = (firstDow == 0) ? 6 : firstDow - 1; // Mon=0
        int daysInMonth = DateTime.DaysInMonth(_calMonth.Year, _calMonth.Month);
        DateTime today = DateTime.Today;

        for (int d = 1; d <= daysInMonth; d++)
        {
            int cellIdx = startOffset + d - 1;
            int row = cellIdx / 7, col = cellIdx % 7;
            var date = new DateTime(_calMonth.Year, _calMonth.Month, d);
            bool isToday = date == today;
            bool hasTasks = tasks.Any(t =>
                (t.PlannedStartDate.HasValue && t.PlannedEndDate.HasValue &&
                 t.PlannedStartDate.Value.Date <= date && t.PlannedEndDate.Value.Date >= date));

            var capturedDate = date;
            var cell = new System.Windows.Controls.StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 30, Margin = new Thickness(0, 1, 0, 1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            cell.MouseLeftButtonUp += (_, _) =>
            {
                _vm.CalendarFocusDate = capturedDate;
                _vm.NavigateToCommand.Execute("Calendar");
            };

            var dayBorder = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = isToday ? todayBg : System.Windows.Media.Brushes.Transparent
            };
            dayBorder.Child = new TextBlock
            {
                Text = d.ToString(), TextAlignment = System.Windows.TextAlignment.Center,
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = isToday ? System.Windows.Media.Brushes.White
                           : (col == 5) ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 120, 180))
                           : (col == 6) ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 80, 80))
                           : fg
            };
            cell.Children.Add(dayBorder);

            if (hasTasks)
                cell.Children.Add(new Border
                {
                    Width = 4, Height = 4, CornerRadius = new CornerRadius(2),
                    Background = taskDot, HorizontalAlignment = HorizontalAlignment.Center
                });

            System.Windows.Controls.Grid.SetRow(cell, row);
            System.Windows.Controls.Grid.SetColumn(cell, col);
            calGrid.Children.Add(cell);
        }
        MiniCalPanel.Children.Add(calGrid);
    }

    // ── マニュアル ───────────────────────────────────
    private void OpenManual_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ManualDialog { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private void OpenChangelog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ChangelogDialog { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    // ══════════════════════════════════════════════════════════
    //  惑星スタイルテンプレート
    // ══════════════════════════════════════════════════════════

    /// <summary>惑星リングの傾き角度（度・時計回り）。右下に若干傾く。</summary>
    private const double PLANET_RING_TILT_DEG = 12.0;
    /// <summary>リングに配置する衛星の数（=日数）。</summary>
    private const int PLANET_SAT_COUNT = 7;
    /// <summary>リング帯の内側半径係数（惑星半径基準・中央線比）。</summary>
    private const double PLANET_RING_INNER_RATIO = 0.72;
    /// <summary>リング帯の外側半径係数（惑星半径基準・中央線比）。</summary>
    private const double PLANET_RING_OUTER_RATIO = 1.28;
    /// <summary>ドラッグで1スロット（=1日分）回転するのに必要な横移動量(px)。</summary>
    private const double PLANET_DRAG_PX_PER_SLOT = 60.0;

    /// <summary>連続回転量（スロット単位、1.0 = 衛星1つ分の回転）。ドラッグ/ホイールで更新。</summary>
    private double _planetPhase = 0.0;
    /// <summary>各物理衛星が現在表す日付オフセット。衛星が惑星裏を通るたびに ±N されて無限スクロールを実現する。</summary>
    private int[]? _planetSatDateOffsets;
    /// <summary>ドラッグ中フラグ。</summary>
    private bool _planetDragging = false;
    /// <summary>ドラッグ直前のカーソル位置。</summary>
    private Point _planetDragLastPos;

    /// <summary>衛星日付配列を初回アクセスで遅延初期化する。</summary>
    private int[] PlanetSatDateOffsets
        => _planetSatDateOffsets ??= System.Linq.Enumerable.Range(0, PLANET_SAT_COUNT).ToArray();

    /// <summary>惑星ビューのテキスト情報を更新し、Canvas を再描画する。</summary>
    private void RefreshPlanetView()
    {
        PlanetProjectText.Text = _vm.IsProjectLoaded ? _vm.ProjectTitle : "TKer";
        PlanetDateText.Text    = DateTime.Now.ToString("yyyy年MM月dd日 (ddd)");
        PlanetClockText.Text   = DateTime.Now.ToString("HH:mm:ss");
        BuildPlanet();
    }

    /// <summary>Canvas サイズ変動時に惑星を再構築する。</summary>
    private void PlanetCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => BuildPlanet();

    /// <summary>惑星・リング・衛星を Canvas 上にレイアウトする。</summary>
    private void BuildPlanet()
    {
        if (PlanetCanvas == null) return;
        double w = PlanetCanvas.ActualWidth;
        double h = PlanetCanvas.ActualHeight;
        if (w < 50 || h < 50) return;

        PlanetCanvas.Children.Clear();

        // ── 配置パラメータ ─────────────────────────────
        double planetR = Math.Min(w, h) * 0.28;
        double cx = w * 0.68;
        double cy = h * 0.52;
        double ringRx = planetR * 1.85;
        double ringRy = planetR * 0.48;
        double tilt  = PLANET_RING_TILT_DEG * Math.PI / 180.0;
        double cosT  = Math.Cos(tilt);
        double sinT  = Math.Sin(tilt);

        // ── 衛星座標と深度を計算 ─────────────────────────
        // 各物理衛星 s は固有の日付オフセット D を持つ。
        // 連続位相 phase に対して、s の角度は (D - phase) * 2π/N。
        // よって phase を増やすと全衛星が CCW 方向に滑らかに回転する。
        // 衛星が惑星裏（angle≈±π）を通過した瞬間に UpdatePlanetPhase で D を ±N して
        // 視覚的に途切れない無限スクロールを実現する。
        var sats = new System.Collections.Generic.List<(double x, double y, double depth, int dateOffset, double normAngle)>();
        for (int s = 0; s < PLANET_SAT_COUNT; s++)
        {
            int D = PlanetSatDateOffsets[s];
            double phi = (D - _planetPhase) * 2 * Math.PI / PLANET_SAT_COUNT;
            double lx = ringRx * Math.Sin(phi);
            double ly = ringRy * Math.Cos(phi);
            double rx = lx * cosT - ly * sinT;
            double ry = lx * sinT + ly * cosT;
            double normAngle = Math.IEEERemainder(phi, 2 * Math.PI);  // (-π, π]
            sats.Add((cx + rx, cy + ry, Math.Cos(phi), D, normAngle));
        }

        // 最前面（angle が 0 に最も近い）衛星を 1 つだけ選んで isFront 扱いとする
        int frontIdx = 0;
        for (int s = 1; s < PLANET_SAT_COUNT; s++)
            if (Math.Abs(sats[s].normAngle) < Math.Abs(sats[frontIdx].normAngle))
                frontIdx = s;

        // ── 1. 後方の衛星（depth<0）を惑星より先に描画 ───
        for (int idx = 0; idx < sats.Count; idx++)
        {
            var s = sats[idx];
            if (s.depth < 0)
                AddSatellite(s.x, s.y, s.depth, s.dateOffset, idx == frontIdx);
        }

        // ── 2. リング後ろ半分 ─────────────────────────
        AddRing(cx, cy, ringRx, ringRy, PLANET_RING_TILT_DEG, behindPlanet: true);

        // ── 3. 惑星本体 ─────────────────────────────────
        AddPlanet(cx, cy, planetR);

        // ── 4. リング前半分 ────────────────────────────
        AddRing(cx, cy, ringRx, ringRy, PLANET_RING_TILT_DEG, behindPlanet: false);

        // ── 5. 前方衛星（depth>=0）─────────────────────
        for (int idx = 0; idx < sats.Count; idx++)
        {
            var s = sats[idx];
            if (s.depth >= 0)
                AddSatellite(s.x, s.y, s.depth, s.dateOffset, idx == frontIdx);
        }
    }

    /// <summary>位相を更新して、各衛星が惑星裏を通過した分だけ日付オフセットを ±N する。</summary>
    private void UpdatePlanetPhase(double newPhase)
    {
        _planetPhase = newPhase;
        var arr = PlanetSatDateOffsets;
        double halfN = PLANET_SAT_COUNT / 2.0;
        for (int s = 0; s < PLANET_SAT_COUNT; s++)
        {
            int D = arr[s];
            // 前進: phase が D + N/2 を超えたら、衛星 s は惑星裏を CCW 方向に抜けた → D += N
            while (_planetPhase > D + halfN) D += PLANET_SAT_COUNT;
            // 後退: phase が D - N/2 を下回ったら、衛星 s は惑星裏を CW 方向に抜けた → D -= N
            while (_planetPhase < D - halfN) D -= PLANET_SAT_COUNT;
            arr[s] = D;
        }
        BuildPlanet();
    }

    /// <summary>地球風グラデーションの円を惑星として配置する。</summary>
    private void AddPlanet(double cx, double cy, double r)
    {
        var planet = new System.Windows.Shapes.Ellipse
        {
            Width = r * 2, Height = r * 2,
            Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.35),
                Center         = new Point(0.5, 0.5),
                RadiusX = 0.7, RadiusY = 0.7,
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x6B, 0xC7, 0xFF), 0.0),
                    new GradientStop(Color.FromRgb(0x2E, 0x7A, 0xD6), 0.45),
                    new GradientStop(Color.FromRgb(0x0E, 0x2E, 0x6A), 0.95),
                    new GradientStop(Color.FromRgb(0x05, 0x14, 0x32), 1.0),
                }
            },
            Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0x6B, 0xC7, 0xFF)),
            StrokeThickness = 0.8,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0x3D, 0x9B, 0xFF),
                BlurRadius = 38, ShadowDepth = 0, Opacity = 0.55
            }
        };
        Canvas.SetLeft(planet, cx - r);
        Canvas.SetTop(planet,  cy - r);
        PlanetCanvas.Children.Add(planet);
    }

    /// <summary>
    /// 傾いた楕円リングを「帯（道）」状の Polygon として描画する。
    /// rx/ry は中央線の半径、内外側半径はそこから ±係数 で生成する。
    /// behindPlanet=true なら奥側半周のみを描画する。
    /// </summary>
    private void AddRing(double cx, double cy, double rx, double ry, double tiltDeg, bool behindPlanet)
    {
        const int segs = 96;
        double tilt = tiltDeg * Math.PI / 180.0;
        double cosT = Math.Cos(tilt), sinT = Math.Sin(tilt);

        double rxIn = rx * PLANET_RING_INNER_RATIO;
        double ryIn = ry * PLANET_RING_INNER_RATIO;
        double rxOut = rx * PLANET_RING_OUTER_RATIO;
        double ryOut = ry * PLANET_RING_OUTER_RATIO;

        // 手前半周: φ ∈ [-π/2, π/2] / 奥半周: φ ∈ [π/2, 3π/2]
        double phiStart = behindPlanet ?  Math.PI / 2 : -Math.PI / 2;
        double phiEnd   = behindPlanet ? 3 * Math.PI / 2 :  Math.PI / 2;

        var pts = new System.Windows.Media.PointCollection();
        // 外側エッジ: phiStart → phiEnd
        for (int i = 0; i <= segs; i++)
        {
            double phi = phiStart + (phiEnd - phiStart) * i / segs;
            double lx = rxOut * Math.Sin(phi);
            double ly = ryOut * Math.Cos(phi);
            pts.Add(new Point(cx + lx * cosT - ly * sinT, cy + lx * sinT + ly * cosT));
        }
        // 内側エッジ: phiEnd → phiStart（逆順で閉じる）
        for (int i = segs; i >= 0; i--)
        {
            double phi = phiStart + (phiEnd - phiStart) * i / segs;
            double lx = rxIn * Math.Sin(phi);
            double ly = ryIn * Math.Cos(phi);
            pts.Add(new Point(cx + lx * cosT - ly * sinT, cy + lx * sinT + ly * cosT));
        }
        if (pts.Count < 3) return;

        // 「土星リング」風の段グラデーション。手前は明るく、奥は暗く落とす。
        byte a1 = behindPlanet ? (byte)0x55 : (byte)0xE6;
        byte a2 = behindPlanet ? (byte)0x44 : (byte)0xCC;
        byte a3 = behindPlanet ? (byte)0x33 : (byte)0x99;
        var fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint   = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(a1, 0xE6, 0xD4, 0xA8), 0.00),
                new GradientStop(Color.FromArgb(a2, 0xCB, 0xB0, 0x82), 0.40),
                new GradientStop(Color.FromArgb(a3, 0x9C, 0x82, 0x5A), 0.75),
                new GradientStop(Color.FromArgb(a3, 0x6B, 0x54, 0x36), 1.00),
            }
        };

        var ring = new System.Windows.Shapes.Polygon
        {
            Points = pts,
            Fill   = fill,
            Stroke = new SolidColorBrush(Color.FromArgb(
                behindPlanet ? (byte)0x44 : (byte)0x88, 0x3A, 0x2C, 0x1A)),
            StrokeThickness = 0.8,
            StrokeLineJoin  = PenLineJoin.Round,
            Opacity = behindPlanet ? 0.55 : 1.0,
        };
        PlanetCanvas.Children.Add(ring);
    }

    /// <summary>
    /// 衛星（1つの日付）を Canvas に配置する。
    /// isFront=true の前面衛星は黄金グラデで強調、それ以外は銀色。
    /// dateOffset==0 の場合は「今日」を表すマーキングを行う。
    /// </summary>
    private void AddSatellite(double x, double y, double depth, int dateOffset, bool isFront)
    {
        // 深度（-1〜+1）→ 0〜1
        double t = (depth + 1) / 2.0;
        bool isActualToday = (dateOffset == 0);

        double size    = isFront ? 56 : 22 + 14 * t;
        double opacity = isFront ? 1.0 : 0.45 + 0.55 * t;

        var date = DateTime.Today.AddDays(dateOffset);

        // 衛星本体
        var orb = new System.Windows.Shapes.Ellipse
        {
            Width = size, Height = size,
            Fill = isFront
                ? new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.35, 0.35),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0xFF, 0xE6, 0xA8), 0.0),
                        new GradientStop(Color.FromRgb(0xFF, 0xA8, 0x3D), 0.55),
                        new GradientStop(Color.FromRgb(0x7A, 0x3C, 0x00), 1.0),
                    }
                }
                : (Brush)new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.35, 0.35),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0xE0, 0xE6, 0xF2), 0.0),
                        new GradientStop(Color.FromRgb(0x9A, 0xA2, 0xB4), 0.7),
                        new GradientStop(Color.FromRgb(0x40, 0x46, 0x55), 1.0),
                    }
                },
            // 「今日」が前面以外にある場合のみシアンの細枠で識別
            Stroke = (!isFront && isActualToday)
                ? new SolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0xFF))
                : null,
            StrokeThickness = (!isFront && isActualToday) ? 2.0 : 0,
            Opacity = opacity,
            Effect = isFront
                ? (System.Windows.Media.Effects.Effect)new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0xC2, 0x55),
                    BlurRadius = 28, ShadowDepth = 0, Opacity = 0.9
                }
                : null,
        };
        Canvas.SetLeft(orb, x - size / 2);
        Canvas.SetTop(orb,  y - size / 2);
        PlanetCanvas.Children.Add(orb);

        // ラベル（日付）
        string label;
        if (isFront)
            label = isActualToday
                ? $"今日\n{date:M/d (ddd)}"
                : $"{date:M/d (ddd)}";
        else
            label = isActualToday
                ? $"★今日\n{date:M/d}"
                : $"{date:M/d}\n({date:ddd})";

        var tb = new TextBlock
        {
            Text = label,
            TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = isFront ? FontWeights.Black : FontWeights.SemiBold,
            FontSize   = isFront ? 13 : 10.5,
            Foreground = isFront
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xC4))
                : (isActualToday
                    ? new SolidColorBrush(Color.FromRgb(0xBE, 0xDF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(
                        (byte)(0xFF * opacity), 0xE6, 0xEA, 0xF2))),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.9
            }
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tw = tb.DesiredSize.Width;
        Canvas.SetLeft(tb, x - tw / 2);
        Canvas.SetTop(tb,  y + size / 2 + 4);
        PlanetCanvas.Children.Add(tb);
    }

    // ── 入力ハンドラ（ホイール/ドラッグで衛星を回転させて日付スクロール）─────
    /// <summary>マウスホイールで位相を 1 スロット分（=1日）進める/戻す。</summary>
    private void PlanetCanvas_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        UpdatePlanetPhase(_planetPhase + (e.Delta > 0 ? 1.0 : -1.0));
        e.Handled = true;
    }

    /// <summary>左ボタン押下でドラッグ開始、マウスをキャプチャしてカーソルを変更する。</summary>
    private void PlanetCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _planetDragging = true;
        _planetDragLastPos = e.GetPosition(PlanetCanvas);
        PlanetCanvas.CaptureMouse();
        PlanetCanvas.Cursor = System.Windows.Input.Cursors.SizeWE;
        e.Handled = true;
    }

    /// <summary>ドラッグ中は横移動量に比例して位相を連続的に更新し、衛星を滑らかに回転させる。</summary>
    private void PlanetCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_planetDragging) return;
        var pos = e.GetPosition(PlanetCanvas);
        double dx = pos.X - _planetDragLastPos.X;
        _planetDragLastPos = pos;
        if (dx != 0)
            UpdatePlanetPhase(_planetPhase + dx / PLANET_DRAG_PX_PER_SLOT);
    }

    /// <summary>マウスアップでドラッグ終了、キャプチャを解放し、位相を最寄りスロットにスナップする。</summary>
    private void PlanetCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_planetDragging) return;
        _planetDragging = false;
        PlanetCanvas.ReleaseMouseCapture();
        PlanetCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
        UpdatePlanetPhase(Math.Round(_planetPhase));
    }

    // ══════════════════════════════════════════════════════════
    //  カードスタイルテンプレート
    // ══════════════════════════════════════════════════════════

    /// <summary>奥に向かって表示するカードの枚数。</summary>
    private const int CARD_VISIBLE_COUNT = 6;
    /// <summary>カード1段ごとの縮小率。</summary>
    private const double CARD_DEPTH_SCALE = 0.84;
    /// <summary>ドラッグで1枚分めくるのに必要な縦移動量(px)。</summary>
    private const double CARD_DRAG_PX_PER_CARD = 90.0;

    /// <summary>連続スクロール位相（1.0 = カード1枚分）。値が増えるほど未来日へ進む。</summary>
    private double _cardPhase = 0.0;
    /// <summary>アニメーションで目指すカード位相。</summary>
    private double _cardPhaseTarget = 0.0;
    /// <summary>カードスクロールのイージングアニメーション用タイマー。</summary>
    private DispatcherTimer? _cardAnimTimer;
    /// <summary>カードドラッグ中フラグ。</summary>
    private bool _cardDragging = false;
    /// <summary>カードドラッグ直前のカーソル位置。</summary>
    private Point _cardDragLastPos;
    /// <summary>スケジュール部品が現在表示している選択日オフセット。変化時のみ再構築する。</summary>
    private int _cardScheduleShownOffset = int.MinValue;
    /// <summary>メディア表示更新用タイマー。</summary>
    private DispatcherTimer? _cardMediaTimer;
    /// <summary>SMTC セッションマネージャー（再生中メディア取得用）。</summary>
    private global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _cardSmtc;

    /// <summary>カードビューのテキスト情報・各ウィジェット部品を更新し、Canvas を再描画する。</summary>
    private void RefreshCardView()
    {
        CardProjectText.Text = _vm.IsProjectLoaded ? _vm.ProjectTitle : "TKer";

        BuildCardShortcuts();
        BuildCardTodo();
        BuildCardTasks();
        BuildCardCollections();
        BuildCardProjects();
        BuildCardAlerts();
        ApplyCardRightLayout();
        _cardScheduleShownOffset = int.MinValue;   // 強制再構築
        BuildCards();
        StartCardMedia();
        ApplyCardSectionThemes();
        ApplyCardLeftOrder();
    }

    // ── 左列の部品 順序・表示／追加・ドラッグ並べ替え ────────────────
    /// <summary>左列の候補部品（現在のレイアウトでは固定配置のため空）。</summary>
    private (string key, FrameworkElement el, string label)[] CardLeftParts()
        => System.Array.Empty<(string, FrameworkElement, string)>();

    /// <summary>設定の順序に従って左列の部品を並べ替え・表示／非表示する。</summary>
    private void ApplyCardLeftOrder()
    {
        var parts = CardLeftParts();
        var saved = _vm.AppSettingsService.CardLeftParts;
        var order = (saved != null && saved.Count > 0)
            ? saved.Where(k => parts.Any(p => p.key == k)).ToList()
            : parts.Select(p => p.key).ToList();   // 既定は全部品を定義順で表示

        // いったん全部品を取り外し、順序どおりに再挿入（順序にないものは非表示）
        foreach (var p in parts) CardLeftStack.Children.Remove(p.el);
        int idx = 0;
        foreach (var key in order)
        {
            var p = parts.First(x => x.key == key);
            p.el.Visibility = Visibility.Visible;
            CardLeftStack.Children.Insert(idx++, p.el);
        }

        // 追加ボタンを末尾へ（編集プレビュー時のみ表示）
        CardLeftStack.Children.Remove(CardAddPartButton);
        CardLeftStack.Children.Add(CardAddPartButton);
        CardAddPartButton.Visibility = IsEditPreview ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>現在の左列の並びを設定へ保存する。</summary>
    private void SaveCardLeftOrder()
    {
        var parts = CardLeftParts();
        var list = new System.Collections.Generic.List<string>();
        foreach (var child in CardLeftStack.Children)
        {
            var p = parts.FirstOrDefault(x => ReferenceEquals(x.el, child));
            if (p.key != null) list.Add(p.key);
        }
        _vm.AppSettingsService.SaveCardLeftParts(list);
    }

    /// <summary>「部品を追加」ボタン: 非表示の部品をメニューから追加する。</summary>
    private void CardAddPart_Click(object sender, RoutedEventArgs e)
    {
        var parts = CardLeftParts();
        var visible = CardLeftStack.Children.OfType<FrameworkElement>().ToList();
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (var p in parts)
        {
            bool shown = visible.Any(v => ReferenceEquals(v, p.el));
            var mi = new System.Windows.Controls.MenuItem { Header = p.label, IsCheckable = true, IsChecked = shown };
            var key = p.key;
            mi.Click += (_, _) => ToggleCardLeftPart(key);
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    /// <summary>指定部品の表示/非表示を切り替えて保存・再構築する。</summary>
    private void ToggleCardLeftPart(string key)
    {
        var current = _vm.AppSettingsService.CardLeftParts;
        var parts = CardLeftParts();
        var list = (current != null && current.Count > 0)
            ? current.Where(k => parts.Any(p => p.key == k)).ToList()
            : parts.Select(p => p.key).ToList();
        if (list.Contains(key)) list.Remove(key);
        else                    list.Add(key);
        _vm.AppSettingsService.SaveCardLeftParts(list);
        ApplyCardLeftOrder();
    }

    // ── ドラッグ並べ替え ──
    private FrameworkElement? _dragPart;
    private Point _dragStart;
    private bool _dragActive;
    private TranslateTransform? _dragTf;

    /// <summary>クリック位置の祖先から左列部品要素を探す。</summary>
    private FrameworkElement? FindLeftPart(object? src)
    {
        var parts = CardLeftParts();
        var node = src as DependencyObject;
        while (node != null)
        {
            if (node is FrameworkElement fe && parts.Any(p => ReferenceEquals(p.el, fe)))
                return fe;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private void LeftStack_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsEditPreview) return;
        var part = FindLeftPart(e.OriginalSource);
        if (part == null) return;
        _dragPart   = part;
        _dragStart  = e.GetPosition(CardLeftStack);
        _dragActive = false;
        CardLeftStack.CaptureMouse();
    }

    private void LeftStack_Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragPart == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var pos = e.GetPosition(CardLeftStack);
        double dy = pos.Y - _dragStart.Y;

        if (!_dragActive)
        {
            if (Math.Abs(dy) < 6) return;
            _dragActive = true;
            _dragTf = new TranslateTransform();
            _dragPart.RenderTransform = _dragTf;
            _dragPart.Opacity = 0.85;
            Panel.SetZIndex(_dragPart, 10);
        }
        _dragTf!.Y = dy;

        // ドラッグ中の中心位置に応じて挿入位置を入れ替える（実際に動いて見える）
        var items = CardLeftStack.Children.OfType<FrameworkElement>()
            .Where(c => !ReferenceEquals(c, CardAddPartButton)).ToList();
        int curIdx = items.IndexOf(_dragPart);
        double dragCenter = pos.Y;
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], _dragPart)) continue;
            var el = items[i];
            var top = el.TranslatePoint(new Point(0, 0), CardLeftStack).Y;
            double center = top + el.ActualHeight / 2;
            if ((i < curIdx && dragCenter < center) || (i > curIdx && dragCenter > center))
            {
                // i の位置へ移動
                CardLeftStack.Children.Remove(_dragPart);
                int insertAt = CardLeftStack.Children.IndexOf(el);
                if (i > curIdx) insertAt++;   // 後方へ動かす場合は対象の後ろへ
                if (insertAt < 0) insertAt = 0;
                CardLeftStack.Children.Insert(Math.Min(insertAt, CardLeftStack.Children.Count), _dragPart);
                _dragStart = pos;     // 再配置後は基準をリセット
                _dragTf!.Y = 0;
                break;
            }
        }
        e.Handled = true;
    }

    private void LeftStack_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_dragPart != null)
        {
            if (_dragActive)
            {
                _dragPart.RenderTransform = null;
                _dragPart.Opacity = 1.0;
                Panel.SetZIndex(_dragPart, 0);
                SaveCardLeftOrder();
            }
            _dragPart = null;
            _dragActive = false;
            CardLeftStack.ReleaseMouseCapture();
        }
    }

    /// <summary>カード各部品にユーザー設定のセクションテーマ（背景/文字/枠/不透明度）を適用する。</summary>
    private void ApplyCardSectionThemes()
    {
        var svc = _vm.AppSettingsService;
        UiThemeHelper.ApplySectionTheme(CardTodoTasksCard,  svc.GetSectionTheme("Card_TodoTasks"));
        UiThemeHelper.ApplySectionTheme(CardScheduleCard,   svc.GetSectionTheme("Card_Schedule"));
        UiThemeHelper.ApplySectionTheme(CardCollectionCard, svc.GetSectionTheme("Card_Collection"));
        UiThemeHelper.ApplySectionTheme(CardProjectsCard,   svc.GetSectionTheme("Card_Projects"));
        UiThemeHelper.ApplySectionTheme(CardAlertCard,      svc.GetSectionTheme("Card_Alert"));
        UiThemeHelper.ApplySectionTheme(CardMediaRoot,      svc.GetSectionTheme("Card_Media"));
        UiThemeHelper.ApplySectionTheme(CardToolsCard,      svc.GetSectionTheme("Card_Tools"));
    }

    /// <summary>Canvas サイズ変動時にカードを再構築する。</summary>
    private void CardCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => BuildCards();

    /// <summary>メディア部品のサイズ変動時に角丸クリップを設定する。</summary>
    private void CardMediaRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border b || b.ActualWidth < 1 || b.ActualHeight < 1) return;
        b.Clip = new System.Windows.Media.RectangleGeometry(
            new Rect(0, 0, b.ActualWidth, b.ActualHeight), 14, 14);
    }

    /// <summary>日付カードを奥行きパースのスタックとして Canvas に配置する。</summary>
    private void BuildCards()
    {
        if (CardCanvas == null) return;
        double w = CardCanvas.ActualWidth;
        double h = CardCanvas.ActualHeight;
        if (w < 80 || h < 80) return;

        CardCanvas.Children.Clear();

        // ── 配置パラメータ（カード幅はウィンドウ幅に追従）──────────
        double cardW = Math.Min(w * 0.34, 340);
        double cardH = cardW * 1.34;
        double stepY = cardH * 0.20;   // 1段ごとの上シフト（縦方向は一定）

        // 前面カードは左端寄り、最奥カードの右端をウィンドウ右端付近に合わせる。
        // stepX（横シフト）をウィンドウ幅から逆算するため、スタックの傾き角度が幅に追従する。
        const double rightMargin = 28;
        double backScale = Math.Pow(CARD_DEPTH_SCALE, CARD_VISIBLE_COUNT);
        double frontX = cardW * 0.5 + 16;
        double backCenterTarget = w - rightMargin - cardW * backScale / 2;
        double stepX = (backCenterTarget - frontX) / CARD_VISIBLE_COUNT;
        if (stepX < cardW * 0.16) stepX = cardW * 0.16;   // 最小間隔（重なりすぎ防止）
        double frontY = h * 0.62;

        double frac = _cardPhase - Math.Floor(_cardPhase); // 0..1
        int    baseOffset = (int)Math.Floor(_cardPhase);

        // 奥（depth 大）から順に描画し、前面（depth≈0）を最後に重ねる。
        // depth = k - frac。frac が増えるとカードが手前(下)へ流れる。
        for (int k = CARD_VISIBLE_COUNT; k >= -1; k--)
        {
            double depth = k - frac;
            if (depth < -1.0 || depth > CARD_VISIBLE_COUNT + 0.5) continue;

            int dateOffset = baseOffset + k;
            AddCard(frontX, frontY, cardW, cardH, depth, stepX, stepY, dateOffset);
        }

        // 年月フォントサイズをカード領域の幅に追従させる
        double monthFont = Math.Clamp(w * 0.07, 36, 96);
        double yearFont  = monthFont * 0.55;
        CardMonthText.FontSize = monthFont;
        CardYearText.FontSize  = yearFont;
        CardMonthText.Margin   = new Thickness(0, -monthFont * 0.25, 0, 0);

        // 前面カードの選択日が変わったら、当日スケジュール・年月・ミニカレンダーを更新する
        int selectedOffset = (int)Math.Round(_cardPhase);
        bool sizeOnly = selectedOffset == _cardScheduleShownOffset;
        if (!sizeOnly)
        {
            _cardScheduleShownOffset = selectedOffset;
            var selDate = DateTime.Today.AddDays(selectedOffset);
            CardYearText.Text  = selDate.ToString("yyyy");
            CardMonthText.Text = selDate.ToString("MM");
            BuildHourlySchedule(selDate);
        }

        // ── 2か月分ミニカレンダーをカード領域の幅に合わせたスケールで再構築 ──
        // セルサイズ・フォントが動的に変わるため、サイズだけの変更でも作り直す
        double calScale = Math.Clamp(w / 1100.0, 0.75, 1.6);
        BuildMiniCalendar(DateTime.Today.AddDays(selectedOffset), calScale);
    }

    /// <summary>1枚の日付カードを奥行き depth に応じてスケール・位置・不透明度を変えて配置する。</summary>
    private void AddCard(double frontX, double frontY, double cardW, double cardH,
                         double depth, double stepX, double stepY, int dateOffset)
    {
        // depth<0（手前に飛び出して退場中）はフェードアウトしつつ拡大
        double scale   = Math.Pow(CARD_DEPTH_SCALE, depth);
        double cx = frontX + depth * stepX;
        double cy = frontY - depth * stepY;
        double opacity = depth >= 0
            ? Math.Max(0.0, 1.0 - depth * 0.16)
            : Math.Max(0.0, 1.0 + depth);          // depth -1→0 で 0→1

        bool isFront = depth >= -0.001 && depth < 1.0;
        var date = DateTime.Today.AddDays(dateOffset);
        bool isActualToday = (dateOffset == 0);

        double sw = cardW * scale;
        double sh = cardH * scale;

        // ── カード本体 ─────────────────────────────────
        var accent = isFront
            ? Color.FromRgb(0xA6, 0x6B, 0xFF)   // 紫
            : Color.FromRgb(0x4A, 0x6B, 0xA8);  // 落ち着いた青

        var border = new Border
        {
            Width = cardW, Height = cardH,
            CornerRadius = new CornerRadius(16),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = isFront
                    ? new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0xF2, 0x3A, 0x2A, 0x66), 0.0),
                        new GradientStop(Color.FromArgb(0xF2, 0x21, 0x17, 0x40), 1.0),
                    }
                    : new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0xCC, 0x1C, 0x24, 0x38), 0.0),
                        new GradientStop(Color.FromArgb(0xCC, 0x12, 0x18, 0x28), 1.0),
                    }
            },
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(isFront ? 2.0 : 1.0),
            Opacity = opacity,
            Effect = isFront
                ? (System.Windows.Media.Effects.Effect)new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = accent, BlurRadius = 36, ShadowDepth = 0, Opacity = 0.7
                }
                : new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.5
                },
        };

        // ── 内容（フォント・余白はカード幅 cardW に比例 → ウィンドウ幅で自動調整）──
        double us = cardW / 300.0;   // 基準幅300pxに対する倍率
        var grid = new Grid { Margin = new Thickness(18 * us, 16 * us, 18 * us, 16 * us) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 上段：曜日 + 今日バッジ
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock
        {
            Text = date.ToString("ddd"),
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = FontWeights.Bold, FontSize = 18 * us,
            Foreground = new SolidColorBrush(
                isFront ? Color.FromRgb(0xE6, 0xD8, 0xFF) : Color.FromRgb(0xAE, 0xC2, 0xE0)),
        });
        if (isActualToday)
        {
            top.Children.Add(new Border
            {
                Margin = new Thickness(8 * us, 1, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6 * us, 1, 6 * us, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "TODAY", FontSize = 10 * us, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x28, 0x00)),
                }
            });
        }
        Grid.SetRow(top, 0);
        grid.Children.Add(top);

        // 中央：日番号のみ（イベント表示は右列の予定部品に集約したため削除）
        var dayNum = new TextBlock
        {
            Text = date.Day.ToString(),
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Black,
            FontSize = 72 * us,
            Foreground = new SolidColorBrush(
                isFront ? Colors.White : Color.FromRgb(0xC8, 0xD4, 0xE8)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(dayNum, 1);
        grid.Children.Add(dayNum);

        // 下段：アクセントバー + 前面カードのサマリーバッジ
        var bottomRow = new DockPanel();
        var accentBar = new Border
        {
            Height = 4 * us, Width = 56 * us, CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush(
                accent, Color.FromArgb(0x33, accent.R, accent.G, accent.B), 0),
        };
        DockPanel.SetDock(accentBar, Dock.Left);
        bottomRow.Children.Add(accentBar);

        if (isFront)
        {
            int evCount = GetScheduleEntries(date).Count;
            int todoCount = _vm.TodoService.GetAll().Count(t => !t.IsCompleted);
            var badges = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            badges.Children.Add(MakeSummaryBadge("📅", evCount, us));
            badges.Children.Add(MakeSummaryBadge("✓",  todoCount, us));
            bottomRow.Children.Add(badges);
        }
        Grid.SetRow(bottomRow, 2);
        grid.Children.Add(bottomRow);

        border.Child = grid;

        // スケール＋配置（原点基準スケールなので左上を中心から逆算）
        border.RenderTransform = new ScaleTransform(scale, scale);
        Canvas.SetLeft(border, cx - sw / 2);
        Canvas.SetTop(border,  cy - sh / 2);
        CardCanvas.Children.Add(border);
    }

    // ── 右列の動的レイアウト（順序・半幅ペア）─────────────────
    /// <summary>右列の部品キー → 実 Border のマップ。</summary>
    private System.Collections.Generic.Dictionary<string, Border> CardRightWidgetMap()
        => new()
        {
            ["Card_Alert"]      = CardAlertCard,
            ["Card_TodoTasks"]  = CardTodoTasksCard,
            ["Card_Schedule"]   = CardScheduleCard,
            ["Card_Projects"]   = CardProjectsCard,
            ["Card_Collection"] = CardCollectionCard,
            ["Card_Media"]      = CardMediaRoot,
        };

    /// <summary>保存済みスロット配置に従って右列の部品を並べ替え・半幅ペア化する。
    /// 編集プレビュー時にはドラッグ並べ替えのハンドラも仕掛ける。
    /// HalfLeft の直後に HalfRight があれば 1 行ペアとして 2 列 Grid に統合。
    /// 単独 HalfLeft / HalfRight はそれぞれ左半分・右半分のみ占有する Grid で表示する。</summary>
    private void ApplyCardRightLayout()
    {
        if (CardRightStack == null) return;
        var map   = CardRightWidgetMap();
        var slots = _vm.AppSettingsService.CardRightSlots
            .Where(s => map.ContainsKey(s.Key))
            .ToList();

        // いったん全部品を親から切り離し
        foreach (var w in map.Values)
            (w.Parent as Panel)?.Children.Remove(w);
        CardRightStack.Children.Clear();

        int i = 0;
        while (i < slots.Count)
        {
            var cur = slots[i];
            bool pair = cur.Mode == "HalfLeft"
                        && i + 1 < slots.Count
                        && slots[i + 1].Mode == "HalfRight";
            if (pair)
            {
                var g = MakeHalfGrid();
                var l = map[cur.Key];
                var r = map[slots[i + 1].Key];
                l.Margin = new Thickness(0, 0, 8, 0);
                r.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(l, 0); Grid.SetColumn(r, 1);
                g.Children.Add(l); g.Children.Add(r);
                CardRightStack.Children.Add(g);
                i += 2;
            }
            else if (cur.Mode == "HalfLeft")
            {
                // 単独 HalfLeft：左半分だけ占有（右半分は空 → ドロップ受け皿）
                var g = MakeHalfGrid();
                var w = map[cur.Key];
                w.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(w, 0);
                g.Children.Add(w);
                CardRightStack.Children.Add(g);
                i++;
            }
            else if (cur.Mode == "HalfRight")
            {
                // 単独 HalfRight：右半分だけ占有
                var g = MakeHalfGrid();
                var w = map[cur.Key];
                w.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(w, 1);
                g.Children.Add(w);
                CardRightStack.Children.Add(g);
                i++;
            }
            else
            {
                var w = map[cur.Key];
                w.Margin = new Thickness(0, 0, 0, 14);
                CardRightStack.Children.Add(w);
                i++;
            }
        }
        if (IsEditPreview)
            HookCardRightDrag();
    }

    /// <summary>半幅行用の 2 列 Grid を生成する。</summary>
    private static Grid MakeHalfGrid()
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 14), Tag = "CardRightHalfRow" };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    /// <summary>右列のドラッグ並べ替え/半幅スナップを有効化する。</summary>
    private void HookCardRightDrag()
    {
        CardRightStack.PreviewMouseLeftButtonDown -= CardRight_Down;
        CardRightStack.PreviewMouseMove           -= CardRight_Move;
        CardRightStack.PreviewMouseLeftButtonUp   -= CardRight_Up;
        CardRightStack.PreviewMouseLeftButtonDown += CardRight_Down;
        CardRightStack.PreviewMouseMove           += CardRight_Move;
        CardRightStack.PreviewMouseLeftButtonUp   += CardRight_Up;
    }

    private Border? _crDrag;
    private Point  _crStart;
    private bool   _crActive;
    private TranslateTransform? _crTf;
    private Border? _crGhost;
    private int    _crDropChildIdx;
    private string _crDropMode = "Full";

    private Border? FindCardRightWidget(object? src)
    {
        var keys = CardRightWidgetMap();
        var n = src as DependencyObject;
        while (n != null)
        {
            if (n is Border b && b.Tag is string t && keys.ContainsKey(t)) return b;
            n = System.Windows.Media.VisualTreeHelper.GetParent(n);
        }
        return null;
    }

    private void CardRight_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsEditPreview) return;
        // ボタンクリックは素通し
        var n = e.OriginalSource as DependencyObject;
        while (n != null)
        {
            if (n is System.Windows.Controls.Primitives.ButtonBase) return;
            n = System.Windows.Media.VisualTreeHelper.GetParent(n);
        }
        var part = FindCardRightWidget(e.OriginalSource);
        if (part == null) return;
        _crDrag = part;
        _crStart = e.GetPosition(CardRightStack);
        _crActive = false;
        CardRightStack.CaptureMouse();
    }

    private void CardRight_Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_crDrag == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var pos = e.GetPosition(CardRightStack);
        double dx = pos.X - _crStart.X;
        double dy = pos.Y - _crStart.Y;
        if (!_crActive)
        {
            if (Math.Abs(dx) < 6 && Math.Abs(dy) < 6) return;
            _crActive = true;
            _crTf = new TranslateTransform();
            _crDrag.RenderTransform = _crTf;
            _crDrag.Opacity = 0.55;
            Panel.SetZIndex(_crDrag, 100);
        }
        // マウスに追従（X+Y 両軸）
        _crTf!.X = dx;
        _crTf!.Y = dy;
        // ゴースト位置を更新
        UpdateGhost(pos);
        e.Handled = true;
    }

    /// <summary>ゴースト要素を初期化（未生成なら生成）。</summary>
    private void EnsureGhost()
    {
        if (_crGhost != null) return;
        _crGhost = new Border
        {
            Height = 64,
            Margin = new Thickness(0, 0, 0, 14),
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0x6B, 0xFF)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xA6, 0x6B, 0xFF)),
            IsHitTestVisible = false,
        };
    }

    /// <summary>ゴーストを親から取り外す。</summary>
    private void RemoveGhost()
    {
        if (_crGhost != null && _crGhost.Parent is Panel p)
            p.Children.Remove(_crGhost);
    }

    /// <summary>ドラッグ位置から挿入先とモードを計算し、ゴーストを移動する。</summary>
    private void UpdateGhost(Point pos)
    {
        if (CardRightStack == null) return;
        EnsureGhost();
        RemoveGhost();

        // ドラッグ中部品は除外して挿入先を探す
        var others = CardRightStack.Children.OfType<UIElement>()
            .Where(c => !ReferenceEquals(c, _crDrag) && !ReferenceEquals(c, _crGhost))
            .ToList();

        double stackW = CardRightStack.ActualWidth;
        double frac   = stackW > 0 ? pos.X / stackW : 0.5;
        string mode = "Full";
        if      (frac < 0.30) mode = "HalfLeft";
        else if (frac > 0.70) mode = "HalfRight";

        // 子要素の Y 範囲から挿入位置を決定
        int insertAtOthers = others.Count;
        for (int i = 0; i < others.Count; i++)
        {
            if (others[i] is not FrameworkElement ch) continue;
            double top;
            try { top = ch.TranslatePoint(new Point(0, 0), CardRightStack).Y; }
            catch { top = 0; }
            double h = ch.ActualHeight;
            if (pos.Y < top + h / 2) { insertAtOthers = i; break; }
        }

        // ゴーストの幅・配置を設定
        if (mode == "HalfLeft")
        {
            _crGhost!.HorizontalAlignment = HorizontalAlignment.Left;
            _crGhost.Width = Math.Max(40, stackW / 2 - 8);
        }
        else if (mode == "HalfRight")
        {
            _crGhost!.HorizontalAlignment = HorizontalAlignment.Right;
            _crGhost.Width = Math.Max(40, stackW / 2 - 8);
        }
        else
        {
            _crGhost!.HorizontalAlignment = HorizontalAlignment.Stretch;
            _crGhost.Width = double.NaN;
        }

        // others ベースの挿入位置を CardRightStack.Children ベースに戻す
        int realIdx = insertAtOthers < others.Count
            ? CardRightStack.Children.IndexOf(others[insertAtOthers])
            : CardRightStack.Children.Count;
        realIdx = Math.Clamp(realIdx, 0, CardRightStack.Children.Count);
        CardRightStack.Children.Insert(realIdx, _crGhost);

        _crDropChildIdx = realIdx;
        _crDropMode     = mode;
    }

    private void CardRight_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_crDrag == null) return;
        if (!_crActive)
        {
            // ドラッグ閾値に達しなかった → クリックとして部品選択を通知
            var clickedKey = _crDrag.Tag as string;
            _crDrag = null;
            CardRightStack.ReleaseMouseCapture();
            if (!string.IsNullOrEmpty(clickedKey))
                RightWidgetClicked?.Invoke(clickedKey);
            return;
        }

        var drag = _crDrag;
        // 視覚効果を戻す
        drag.RenderTransform = null;
        drag.Opacity = 1.0;
        Panel.SetZIndex(drag, 0);

        var slots = _vm.AppSettingsService.CardRightSlots
            .Where(s => CardRightWidgetMap().ContainsKey(s.Key)).ToList();
        string dragKey = (string)drag.Tag;
        int dragIdx = slots.FindIndex(s => s.Key == dragKey);
        if (dragIdx < 0) { ClearDragState(); return; }

        // ゴースト位置（CardRightStack 子要素 index）をスロット index へ変換
        // ※ ゴースト自身と被ドラッグ要素を除外して数える
        int ghostChildIdx = _crDropChildIdx;
        var allChildren = CardRightStack.Children.OfType<UIElement>().ToList();
        // 「ゴーストの直前にある実 child の数」を数える
        int childCountBeforeGhost = 0;
        for (int i = 0; i < ghostChildIdx && i < allChildren.Count; i++)
        {
            var ch = allChildren[i];
            if (ReferenceEquals(ch, _crGhost) || ReferenceEquals(ch, drag)) continue;
            childCountBeforeGhost++;
        }

        // 元のスロット配列で、被ドラッグスロットを除いた状態における
        // 子 index (childCountBeforeGhost) に対応するスロット index を求める
        var without = new System.Collections.Generic.List<CardRightSlot>(slots);
        without.RemoveAt(dragIdx);
        int targetSlotIdx = ChildIdxToSlotIdx(childCountBeforeGhost, without);

        // スロット配列を更新
        slots.RemoveAt(dragIdx);
        var newSlot = new CardRightSlot { Key = dragKey, Mode = _crDropMode };
        targetSlotIdx = Math.Clamp(targetSlotIdx, 0, slots.Count);
        slots.Insert(targetSlotIdx, newSlot);

        _vm.AppSettingsService.SaveCardRightSlots(slots);
        ClearDragState();
        ApplyCardRightLayout();
    }

    /// <summary>ドラッグ状態のクリーンアップ。</summary>
    private void ClearDragState()
    {
        RemoveGhost();
        _crDrag = null;
        _crActive = false;
        if (CardRightStack.IsMouseCaptured) CardRightStack.ReleaseMouseCapture();
    }

    /// <summary>スロット列のうち何個目のスロット位置が、表示上の子要素 index に対応するかを返す。
    /// HalfLeft+HalfRight ペアは 1 子要素で 2 スロット消費する。</summary>
    private static int ChildIdxToSlotIdx(int childIdx, System.Collections.Generic.List<CardRightSlot> slots)
    {
        int curChild = 0;
        int si = 0;
        while (si < slots.Count && curChild < childIdx)
        {
            bool pair = slots[si].Mode == "HalfLeft"
                        && si + 1 < slots.Count
                        && slots[si + 1].Mode == "HalfRight";
            si += pair ? 2 : 1;
            curChild++;
        }
        return si;
    }

    // ── ウィジェット部品ビルダー ─────────────────────────────
    /// <summary>共通カラー: 二次テキスト。</summary>
    private static Brush CardDimBrush => new SolidColorBrush(Color.FromRgb(0x9A, 0xA2, 0xB4));

    /// <summary>前面カードに置くサマリーバッジ（アイコン＋件数）を生成する。</summary>
    private static Border MakeSummaryBadge(string icon, int count, double us)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(10 * us),
            Padding = new Thickness(8 * us, 2 * us, 8 * us, 2 * us),
            Margin = new Thickness(6 * us, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x88, 0xA6, 0x6B, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"{icon} {count}", FontSize = 11 * us, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
            },
        };
    }

    /// <summary>Canvas サイズ変動時にショートカットラインを再構築する。</summary>
    private void CardShortcutCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => BuildCardShortcuts();

    /// <summary>
    /// ショートカットを「全幅の横ライン上に並ぶ円形ノード」として描画する。
    /// 各ノードに実アプリアイコンとアプリ名を表示し、クリックで起動する。
    /// </summary>
    private void BuildCardShortcuts()
    {
        if (CardShortcutCanvas == null) return;
        CardShortcutCanvas.Children.Clear();

        double w = CardShortcutCanvas.ActualWidth;
        double h = CardShortcutCanvas.ActualHeight;
        if (w < 40 || h < 20) return;

        double cy = h * 0.40;   // ライン縦位置（下側に名前スペースを残す）

        // ── 横ライン（画面左端〜右端）──
        var line = new System.Windows.Shapes.Line
        {
            X1 = 0, Y1 = cy, X2 = w, Y2 = cy,
            StrokeThickness = 40,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
            Stroke = new LinearGradientBrush(
                Color.FromArgb(0x33, 0xA6, 0x6B, 0xFF),
                Color.FromArgb(0xCC, 0xA6, 0x6B, 0xFF), 0)
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x22, 0xA6, 0x6B, 0xFF), 0.0),
                    new GradientStop(Color.FromArgb(0xCC, 0xA6, 0x6B, 0xFF), 0.5),
                    new GradientStop(Color.FromArgb(0x22, 0xA6, 0x6B, 0xFF), 1.0),
                }
            },
        };
        CardShortcutCanvas.Children.Add(line);

        var shortcuts = _vm.AppSettingsService.Shortcuts;
        // 編集プレビュー時は末尾に「＋（追加）」ノードを置くため、0件でも描画する
        if (shortcuts.Count == 0 && !IsEditPreview) return;

        const double diameter = 48;
        double left  = 60;
        double right = w - 60;
        if (right < left) { left = 30; right = w - 30; }
        int n = shortcuts.Count;
        int total = n + (IsEditPreview ? 1 : 0);   // ＋ノード分

        for (int slot = 0; slot < total; slot++)
        {
            double x = total == 1 ? (left + right) / 2
                                  : left + (right - left) * slot / (total - 1);
            bool isAddNode = IsEditPreview && slot == n;

            // 円形ノード
            var circle = new Border
            {
                Width = diameter, Height = diameter,
                CornerRadius = new CornerRadius(diameter / 2),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1F, 0x2E)),
                BorderBrush = new SolidColorBrush(isAddNode
                    ? Color.FromRgb(0x52, 0x9E, 0x72) : Color.FromRgb(0xA6, 0x6B, 0xFF)),
                BorderThickness = new Thickness(2),
                Cursor = System.Windows.Input.Cursors.Hand,
                ClipToBounds = true,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = isAddNode ? Color.FromRgb(0x52, 0x9E, 0x72) : Color.FromRgb(0xA6, 0x6B, 0xFF),
                    BlurRadius = 14, ShadowDepth = 0, Opacity = 0.6
                },
            };

            if (isAddNode)
            {
                circle.ToolTip = "ショートカットアプリを追加";
                circle.Child = new TextBlock
                {
                    Text = "＋", FontSize = 26, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0xD0, 0x9A)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                circle.MouseLeftButtonUp += (_, e) => { e.Handled = true; AddShortcutViaDialog(); };
                Canvas.SetLeft(circle, x - diameter / 2);
                Canvas.SetTop(circle,  cy - diameter / 2);
                CardShortcutCanvas.Children.Add(circle);

                var addLabel = new TextBlock
                {
                    Text = "追加", FontSize = 11, TextAlignment = TextAlignment.Center,
                    Width = 96, Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0xD0, 0x9A)),
                };
                Canvas.SetLeft(addLabel, x - 48);
                Canvas.SetTop(addLabel,  cy + diameter / 2 + 5);
                CardShortcutCanvas.Children.Add(addLabel);
                continue;
            }

            var sc = shortcuts[slot];
            circle.ToolTip = sc.Path;
            // 実アプリアイコン（取得できなければ絵文字アイコンにフォールバック）
            var iconSrc = GetShellIcon(sc.Path);
            if (iconSrc != null)
            {
                var iconImg = new Image
                {
                    Source = iconSrc, Width = 28, Height = 28, Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                RenderOptions.SetBitmapScalingMode(iconImg, BitmapScalingMode.HighQuality);
                circle.Child = iconImg;
            }
            else
            {
                circle.Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(sc.Icon) ? "🔗" : sc.Icon, FontSize = 20,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
            }
            var path = sc.Path;
            circle.MouseLeftButtonUp += (_, _) => OpenShortcut(path);
            Canvas.SetLeft(circle, x - diameter / 2);
            Canvas.SetTop(circle,  cy - diameter / 2);
            CardShortcutCanvas.Children.Add(circle);

            // アプリ名（円の下に中央寄せ）
            var name = new TextBlock
            {
                Text = sc.Name, FontSize = 11, TextAlignment = TextAlignment.Center,
                Width = 96, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
            };
            Canvas.SetLeft(name, x - 48);
            Canvas.SetTop(name,  cy + diameter / 2 + 5);
            CardShortcutCanvas.Children.Add(name);
        }
    }

    /// <summary>ファイル選択ダイアログでアプリ/ファイルを選び、ショートカットとして追加する。</summary>
    private void AddShortcutViaDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "ショートカットに割り当てるアプリ／ファイルを選択",
            Filter = "アプリ・ファイル (*.exe;*.lnk;*.*)|*.exe;*.lnk;*.*",
        };
        if (dlg.ShowDialog() != true) return;

        var list = _vm.AppSettingsService.Shortcuts.ToList();
        list.Add(new TKer.Models.AppShortcut
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
            Path = dlg.FileName,
            Icon = "🔗",
        });
        _vm.AppSettingsService.SaveShortcuts(list);
        BuildCardShortcuts();
    }

    // ── シェルアイコン取得（SHGetFileInfo）─────────────────────────
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    /// <summary>指定パスのファイル/アプリに関連付けられたシェルアイコンを取得する（失敗時 null）。</summary>
    private static System.Windows.Media.Imaging.BitmapSource? GetShellIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        const uint SHGFI_ICON = 0x000000100, SHGFI_LARGEICON = 0x000000000;
        var info = new SHFILEINFO();
        try
        {
            var res = SHGetFileInfo(path, 0, ref info,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);
            if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon, System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DestroyIcon(info.hIcon); }
        }
        catch { return null; }
    }

    /// <summary>Base64 文字列を BitmapImage に変換する。空・失敗時は null。</summary>
    private static System.Windows.Media.Imaging.BitmapImage? DecodeBase64Image(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        try
        {
            var bytes = Convert.FromBase64String(data);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = new System.IO.MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>ToDo 部品（右列上）を再構築する（未完了を優先表示）。</summary>
    private void BuildCardTodo()
    {
        CardTodoPanel.Children.Clear();
        // 全件を表示する。3 行超過分は内部 ScrollViewer でスクロール。
        var todos = _vm.TodoService.GetAll().Where(t => !t.IsCompleted).ToList();
        CardNoTodoText.Visibility = todos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var todo in todos)
        {
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 5), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x2A, 0x30, 0x40)),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center,
                Fill = ParseBrushSafe(todo.Color, Color.FromRgb(0x3D, 0x7E, 0xFF)),
            };
            Grid.SetColumn(dot, 0);
            g.Children.Add(dot);

            var title = new TextBlock
            {
                Text = todo.Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
            };
            Grid.SetColumn(title, 1);
            g.Children.Add(title);

            if (!string.IsNullOrEmpty(todo.DueDateLabel))
            {
                var due = new TextBlock
                {
                    Text = todo.DueDateLabel, FontSize = 11, FontFamily = new FontFamily("Consolas"),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
                    Foreground = todo.IsOverdue
                        ? new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50))
                        : CardDimBrush,
                };
                Grid.SetColumn(due, 2);
                g.Children.Add(due);
            }
            row.Child = g;
            var id = todo.Id;
            row.MouseLeftButtonUp += (_, _) => { _vm.TodoService.Toggle(id); BuildCardTodo(); };
            CardTodoPanel.Children.Add(row);
        }
    }

    // ── 時間単位の予定部品 ─────────────────────────────────
    /// <summary>選択日のスケジュール部品を 0:00〜23:00 の時間単位リストとして再構築し、
    /// 当日の場合は現在時刻の行を一番上にスクロールする。</summary>
    private void BuildHourlySchedule(DateTime date)
    {
        if (CardSchedulePanel == null) return;
        CardSchedulePanel.Children.Clear();
        var events = GetScheduleEntries(date);

        var hourRows = new System.Collections.Generic.List<FrameworkElement>();
        for (int h = 0; h < 24; h++)
        {
            int hour = h;
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 2), Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(
                    hour == DateTime.Now.Hour && date.Date == DateTime.Today
                        ? (byte)0x66 : (byte)0x22, 0x2A, 0x2A, 0x66)),
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var time = new TextBlock
            {
                Text = $"{hour:00}:00", FontFamily = new FontFamily("Consolas"),
                FontSize = 11, FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBE, 0xC8, 0xDC)),
            };
            Grid.SetColumn(time, 0);
            g.Children.Add(time);

            // この時間帯（h:00〜h+1:00）に該当するイベントを列挙
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (var ev in _vm.ScheduleService.GetByDate(date))
            {
                if (ev.IsAllDay && hour == 0)
                {
                    sp.Children.Add(MakeScheduleEvLine("終日", ev.Title,
                        ParseColorSafe(ev.Color, Color.FromRgb(0x3D, 0x7E, 0xFF))));
                }
                else if (!ev.IsAllDay && ev.StartTime.Date == date.Date && ev.StartTime.Hour == hour)
                {
                    sp.Children.Add(MakeScheduleEvLine(
                        $"{ev.StartTime:HH:mm}～{ev.EndTime:HH:mm}", ev.Title,
                        ParseColorSafe(ev.Color, Color.FromRgb(0x3D, 0x7E, 0xFF))));
                }
            }
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            row.Child = g;

            hourRows.Add(row);
            CardSchedulePanel.Children.Add(row);
        }

        // 当日の場合は現在の時間帯を一番上に表示
        if (date.Date == DateTime.Today && CardScheduleScroll != null)
        {
            int idx = Math.Clamp(DateTime.Now.Hour, 0, hourRows.Count - 1);
            Dispatcher.BeginInvoke(new Action(() =>
                hourRows[idx].BringIntoView()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>予定行内のイベント表記（バー＋時刻＋名前）を生成する。</summary>
    private static FrameworkElement MakeScheduleEvLine(string time, string title, Color color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        row.Children.Add(new Border
        {
            Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 1, 7, 1),
            Background = new SolidColorBrush(color),
        });
        row.Children.Add(new TextBlock
        {
            Text = time, FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            Foreground = new SolidColorBrush(color),
        });
        row.Children.Add(new TextBlock
        {
            Text = title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
        });
        return row;
    }

    // ── 2か月分ミニカレンダー（枠なし背景なし）─────────────────
    /// <summary>選択日の当月＋翌月のミニカレンダーをカード領域右下に表示する。
    /// scale はウィンドウ幅に応じたセル/フォントの倍率（既定 1.0）。</summary>
    private void BuildMiniCalendar(DateTime date, double scale = 1.0)
    {
        if (CardMiniCalendarHost == null) return;
        CardMiniCalendarHost.Children.Clear();
        var m1 = new DateTime(date.Year, date.Month, 1);
        var m2 = m1.AddMonths(1);
        CardMiniCalendarHost.Children.Add(BuildOneMonth(m1, date, scale));
        CardMiniCalendarHost.Children.Add(BuildOneMonth(m2, date, scale));
    }

    /// <summary>指定月のミニカレンダー（タイトル＋曜日＋日付グリッド）を生成する。
    /// scale でセル/フォントを倍率調整。</summary>
    private static FrameworkElement BuildOneMonth(DateTime month, DateTime selected, double scale = 1.0)
    {
        double cellW    = 30 * scale;
        double cellH    = 26 * scale;
        double cellInW  = cellW * 0.86;
        double cellInH  = cellH * 0.92;
        double headFont = 14 * scale;
        double dayFont  = 12 * scale;

        var root = new StackPanel { Margin = new Thickness(0, 0, 28 * scale, 0) };
        root.Children.Add(new TextBlock
        {
            Text = month.ToString("yyyy / MM"), FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = FontWeights.Bold, FontSize = headFont, Margin = new Thickness(0, 0, 0, 6 * scale),
            Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
        });

        var grid = new Grid();
        for (int d = 0; d < 7; d++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cellW) });
        for (int r = 0; r < 7; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cellH) });

        string[] dow = { "日", "月", "火", "水", "木", "金", "土" };
        for (int d = 0; d < 7; d++)
        {
            var tb = new TextBlock
            {
                Text = dow[d], FontSize = dayFont, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(d == 0 ? Color.FromRgb(0xE8, 0x70, 0x70)
                                              : d == 6 ? Color.FromRgb(0x70, 0xA0, 0xE8)
                                              : Color.FromRgb(0x9A, 0xA2, 0xB4)),
            };
            Grid.SetRow(tb, 0); Grid.SetColumn(tb, d);
            grid.Children.Add(tb);
        }

        int firstDow = (int)new DateTime(month.Year, month.Month, 1).DayOfWeek;
        int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var today = DateTime.Today;
        for (int day = 1; day <= daysInMonth; day++)
        {
            int idx = firstDow + day - 1;
            int row = idx / 7 + 1;
            int col = idx % 7;
            var date = new DateTime(month.Year, month.Month, day);

            bool isToday    = date == today;
            bool isSelected = date.Date == selected.Date;

            var cellBorder = new Border
            {
                CornerRadius = new CornerRadius(cellInH / 2),
                Width = cellInW, Height = cellInH,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = isSelected
                    ? new SolidColorBrush(Color.FromRgb(0xA6, 0x6B, 0xFF))
                    : (isToday ? new SolidColorBrush(Color.FromArgb(0x55, 0xA6, 0x6B, 0xFF))
                               : (Brush?)null!),
                Child = new TextBlock
                {
                    Text = day.ToString(), FontSize = dayFont, TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = isSelected
                        ? new SolidColorBrush(Colors.White)
                        : new SolidColorBrush(col == 0 ? Color.FromRgb(0xE8, 0x90, 0x90)
                                            : col == 6 ? Color.FromRgb(0x90, 0xB0, 0xE8)
                                            : Color.FromRgb(0xCF, 0xCF, 0xCF)),
                },
            };
            Grid.SetRow(cellBorder, row); Grid.SetColumn(cellBorder, col);
            grid.Children.Add(cellBorder);
        }
        root.Children.Add(grid);
        return root;
    }

    // ── アラート部品 ─────────────────────────────────────
    /// <summary>全プロジェクトのアラート（期限超過・締切間近・未着手超過）を一覧表示する。</summary>
    private void BuildCardAlerts()
    {
        if (CardAlertPanel == null) return;
        CardAlertPanel.Children.Clear();
        var alerts = _vm.AppSettingsService.CollectAlerts();
        CardNoAlertText.Visibility = alerts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 全件を表示。3 行超過分は内部 ScrollViewer でスクロール。
        foreach (var a in alerts)
        {
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 5), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x2A, 0x30, 0x40)),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            g.Children.Add(new TextBlock
            {
                Text = a.LevelIcon, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
            });

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = a.TaskName, FontSize = 12, FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"{a.ProjectName} · {a.LevelLabel}", FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA2, 0xB4)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);

            var lvlColor = a.Level switch
            {
                AlertLevel.Overdue    => Color.FromRgb(0xEF, 0x53, 0x50),
                AlertLevel.DueSoon    => Color.FromRgb(0xFF, 0xC2, 0x55),
                AlertLevel.NotStarted => Color.FromRgb(0xFF, 0xA8, 0x3D),
                _                     => Color.FromRgb(0x88, 0x92, 0xA6),
            };
            var remain = new TextBlock
            {
                Text = a.RemainingLabel, FontFamily = new FontFamily("Consolas"),
                FontSize = 11, FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(lvlColor),
            };
            Grid.SetColumn(remain, 2);
            g.Children.Add(remain);

            row.Child = g;
            var path = a.DataFilePath;
            row.MouseLeftButtonUp += (_, _) =>
            {
                _vm.SwitchProjectCommand.Execute(path);
                _vm.NavigateToCommand.Execute("TaskList");
            };
            CardAlertPanel.Children.Add(row);
        }
    }

    /// <summary>「すべて ▶」: アラート一覧ダイアログを開く。</summary>
    private void CardAlerts_Click(object sender, RoutedEventArgs e)
        => _vm.ShowAlertsCommand.Execute(null);

    // ── コレクション部品 ─────────────────────────────────────
    /// <summary>コレクション一覧をカバー画像 or アイコンのカードで表示する。
    /// AppSettings.CardCollectionFilter に登録された ID のみ表示する（空＝何も表示しない）。
    /// 編集プレビュー時はカード末尾に「＋コレクションを追加」ボタンを表示する。</summary>
    private void BuildCardCollections()
    {
        if (CardCollectionPanel == null) return;
        CardCollectionPanel.Children.Clear();
        var all    = _vm.CollectionService.Collections;
        var filter = _vm.AppSettingsService.CardCollectionFilter ?? new System.Collections.Generic.List<string>();
        var cols   = filter.Count == 0
            ? System.Linq.Enumerable.Empty<Collection>().ToList()
            : all.Where(c => filter.Contains(c.Id)).ToList();
        CardNoCollectionText.Visibility = cols.Count == 0 && !IsEditPreview ? Visibility.Visible : Visibility.Collapsed;
        CardCollectionAddBtn.Visibility = IsEditPreview ? Visibility.Visible : Visibility.Collapsed;

        foreach (var col in cols)
        {
            var card = new Border
            {
                Width = 84, Height = 100, Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x12, 0x18, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xA6, 0x6B, 0xFF)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ClipToBounds = true, ToolTip = col.Name,
            };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cover = new Border
            {
                CornerRadius = new CornerRadius(6), Margin = new Thickness(5, 5, 5, 2),
                Background = new SolidColorBrush(Color.FromArgb(0x55, 0x2A, 0x2A, 0x66)),
            };
            var img = DecodeBase64Image(col.CoverImageData);
            if (img != null)
                cover.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            else
                cover.Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(col.Icon) ? "📁" : col.Icon, FontSize = 26,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            Grid.SetRow(cover, 0); g.Children.Add(cover);
            var name = new TextBlock
            {
                Text = col.Name, FontSize = 10, FontWeight = FontWeights.Bold,
                Margin = new Thickness(6, 0, 6, 5), TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
            };
            Grid.SetRow(name, 1); g.Children.Add(name);
            card.Child = g;
            var navCol = col;
            card.MouseLeftButtonUp += (_, _) =>
            {
                _vm.SelectedCollection = navCol;
                _vm.NavigateToCommand.Execute("CollectionItems");
            };
            CardCollectionPanel.Children.Add(card);
        }
    }

    // ── プロジェクト部品 ─────────────────────────────────────
    /// <summary>最近のプロジェクト一覧（サマリー）を行で表示する。</summary>
    private void BuildCardProjects()
    {
        if (CardProjectsPanel == null) return;
        CardProjectsPanel.Children.Clear();
        var summaries = _vm.AppSettingsService.CollectSummaries();
        CardNoProjectsText.Visibility = summaries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // 全件を表示。3 行超過分は内部 ScrollViewer でスクロール。
        foreach (var s in summaries)
        {
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 5), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x2A, 0x30, 0x40)),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = s.Entry.ProjectName, FontSize = 12, FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"タスク {s.TotalTasks} / 完了 {s.DoneTasks}", FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA2, 0xB4)),
            });
            Grid.SetColumn(sp, 0); g.Children.Add(sp);
            var prog = new TextBlock
            {
                Text = s.ProgressLabel, FontFamily = new FontFamily("Consolas"),
                FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = new SolidColorBrush(s.HasAlert
                    ? Color.FromRgb(0xEF, 0x53, 0x50)
                    : Color.FromRgb(0x52, 0x9E, 0x72)),
            };
            Grid.SetColumn(prog, 1); g.Children.Add(prog);
            row.Child = g;
            var path = s.Entry.DataFilePath;
            row.MouseLeftButtonUp += (_, _) => _vm.SwitchProjectCommand.Execute(path);
            CardProjectsPanel.Children.Add(row);
        }
    }

    private void CardGoToCollection_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("Collection");
    private void CardGoToProjects_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("ProjectList");

    /// <summary>編集プレビュー時の「＋コレクションを追加」: 表示対象のコレクションを選ぶダイアログを開く。</summary>
    private void CardCollectionAdd_Click(object sender, RoutedEventArgs e)
    {
        var all    = _vm.CollectionService.Collections;
        var filter = _vm.AppSettingsService.CardCollectionFilter ?? new System.Collections.Generic.List<string>();
        var selected = new System.Collections.Generic.HashSet<string>(filter);

        var dlg = new Window
        {
            Title = "表示するコレクションを選択",
            Width = 360, Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = (Brush)Application.Current.Resources["BgSecondaryBrush"],
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
        };
        var root = new DockPanel { Margin = new Thickness(16) };

        var foot = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var okBtn = new Button
        {
            Content = "OK", Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.Resources["PrimaryButton"],
        };
        var cancelBtn = new Button
        {
            Content = "キャンセル", Padding = new Thickness(18, 6, 18, 6),
            Style = (Style)Application.Current.Resources["SecondaryButton"],
        };
        foot.Children.Add(okBtn); foot.Children.Add(cancelBtn);
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var listStack = new StackPanel();
        var checks = new System.Collections.Generic.List<(string id, CheckBox cb)>();
        foreach (var col in all)
        {
            var cb = new CheckBox
            {
                Content = col.Name, Margin = new Thickness(0, 4, 0, 4), FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                IsChecked = selected.Contains(col.Id),
            };
            listStack.Children.Add(cb);
            checks.Add((col.Id, cb));
        }
        if (all.Count == 0)
            listStack.Children.Add(new TextBlock
            {
                Text = "登録されたコレクションがありません", FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA2, 0xB4)),
            });
        listScroll.Content = listStack;
        root.Children.Add(listScroll);

        okBtn.Click += (_, _) =>
        {
            var ids = checks.Where(p => p.cb.IsChecked == true).Select(p => p.id).ToList();
            _vm.AppSettingsService.SaveCardCollectionFilter(ids);
            BuildCardCollections();
            dlg.DialogResult = true;
        };
        cancelBtn.Click += (_, _) => dlg.DialogResult = false;

        dlg.Content = root;
        dlg.ShowDialog();
    }

    /// <summary>アクティブプロジェクトのタスク一覧部品を再構築する（未完了を優先）。</summary>
    private void BuildCardTasks()
    {
        CardTaskPanel.Children.Clear();
        var project = _vm.ProjectService.CurrentProject;
        if (project == null || project.Tasks.Count == 0)
        {
            CardNoTaskText.Visibility = Visibility.Visible;
            return;
        }
        CardNoTaskText.Visibility = Visibility.Collapsed;
        // 全件を表示。3 行超過分は内部 ScrollViewer でスクロール。
        var tasks = project.Tasks
            .Where(t => t.Status != "完了")
            .OrderByDescending(t => t.UpdatedAt)
            .ToList();
        if (tasks.Count == 0) tasks = project.Tasks.OrderByDescending(t => t.UpdatedAt).ToList();

        foreach (var task in tasks)
        {
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 5), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x2A, 0x30, 0x40)),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = task.Name, FontSize = 12, FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
            });
            var sub = task.PlannedEndDate.HasValue
                ? $"期限 {task.PlannedEndDate.Value:MM/dd}  ·  {task.Assignee}"
                : task.Assignee;
            sp.Children.Add(new TextBlock
            {
                Text = sub, FontSize = 10, Margin = new Thickness(0, 2, 0, 0),
                Foreground = CardDimBrush, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(sp, 0);
            g.Children.Add(sp);

            var statusColor = task.Status switch
            {
                "対応中"     => Color.FromRgb(0x2E, 0x9B, 0xFF),
                "レビュー中" => Color.FromRgb(0xB0, 0x6B, 0xFF),
                _            => Color.FromRgb(0x88, 0x92, 0xA6),
            };
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(0x33, statusColor.R, statusColor.G, statusColor.B)),
                Child = new TextBlock
                {
                    Text = task.Status, FontSize = 10, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(statusColor),
                },
            };
            Grid.SetColumn(badge, 1);
            g.Children.Add(badge);

            row.Child = g;
            row.MouseLeftButtonUp += (_, _) => _vm.NavigateToCommand.Execute("TaskList");
            CardTaskPanel.Children.Add(row);
        }
    }

    /// <summary>指定日の予定（イベント＋該当タスク）を時刻順の (時刻, ラベル, 色) で返す。</summary>
    private List<(string time, string label, Color color)> GetScheduleEntries(DateTime date)
    {
        var rows = new List<(string time, string label, Color color)>();
        foreach (var ev in _vm.ScheduleService.GetByDate(date))
        {
            string time = ev.IsAllDay ? "終日" : $"{ev.StartTime:HH:mm}～{ev.EndTime:HH:mm}";
            rows.Add((time, ev.Title, ParseColorSafe(ev.Color, Color.FromRgb(0x3D, 0x7E, 0xFF))));
        }
        var project = _vm.ProjectService.CurrentProject;
        if (project != null)
        {
            foreach (var tsk in project.Tasks.Where(tt =>
                tt.PlannedStartDate.HasValue && tt.PlannedEndDate.HasValue &&
                tt.PlannedStartDate.Value.Date <= date.Date && tt.PlannedEndDate.Value.Date >= date.Date))
            {
                rows.Add(("予定", tsk.Name, Color.FromRgb(0x52, 0x9E, 0x72)));
            }
        }
        return rows.OrderBy(r => r.time).ToList();
    }

    // ── メディア表示部品（SMTC ポーリング・ポップアップと同一外観）──────
    /// <summary>直近に表示したメディアのタイトル（曲変更検出用）。</summary>
    private string? _cardLastMediaTitle;
    /// <summary>直近に表示したメディアのアプリ AUMID（アイコン更新検出用）。</summary>
    private string? _cardLastMediaAumid;

    /// <summary>SMTC を初期化し、再生中メディアのポーリングを開始する。</summary>
    private async void StartCardMedia()
    {
        if (_cardMediaTimer == null)
        {
            try
            {
                _cardSmtc ??= await global::Windows.Media.Control
                    .GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            }
            catch
            {
                // WinRT 非対応環境では機能を無効化
                CardMediaTitle.Text = "メディア情報を取得できません";
                return;
            }
            _cardMediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _cardMediaTimer.Tick += async (_, _) => await RefreshCardMedia();
        }
        _cardMediaTimer.Start();
        await RefreshCardMedia();
    }

    /// <summary>現在の SMTC セッションからタイトル・サムネイル・タイムラインを取得して表示を更新する。</summary>
    private async Task RefreshCardMedia()
    {
        try
        {
            var session = _cardSmtc?.GetCurrentSession();
            if (session == null) { ClearCardMedia(); return; }

            var props = await session.TryGetMediaPropertiesAsync();
            var info  = session.GetPlaybackInfo();
            var title = props?.Title ?? "";
            if (string.IsNullOrWhiteSpace(title)) { ClearCardMedia(); return; }

            bool playing = info?.PlaybackStatus
                == global::Windows.Media.Control
                    .GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // 再生アプリが変わったらアイコンを更新
            var aumid = session.SourceAppUserModelId ?? "";
            if (aumid != _cardLastMediaAumid)
            {
                _cardLastMediaAumid = aumid;
                _ = UpdateCardAppIcon(aumid);
            }

            // 曲が変わったらテキスト・背景を作り直す
            if (title != _cardLastMediaTitle)
            {
                _cardLastMediaTitle  = title;
                CardMediaTitle.Text  = title;
                CardMediaArtist.Text = props?.Artist ?? "";
                // 左カード領域のコンパクトメディア欄にも反映
                if (CardLeftMediaTitle != null)  CardLeftMediaTitle.Text  = title;
                if (CardLeftMediaArtist != null) CardLeftMediaArtist.Text = props?.Artist ?? "";
                _ = UpdateCardMediaBackground(props);
            }

            UpdateCardMediaTimeline(session, playing);
        }
        catch
        {
            ClearCardMedia();
        }
    }

    /// <summary>メディア表示を初期状態（再生なし）に戻す。</summary>
    private void ClearCardMedia()
    {
        CardMediaTitle.Text   = "再生中のメディアはありません";
        CardMediaArtist.Text  = "";
        CardMediaFill.Width    = 0;
        CardMediaCurTime.Text  = "0:00";
        CardMediaTotTime.Text  = "0:00";
        CardMediaBgImage.Source = null;
        CardMediaAppIcon.Source = null;
        // 左カード領域のメディア欄（右列と同外観）もクリア
        if (CardLeftMediaTitle   != null) CardLeftMediaTitle.Text     = "再生中のメディアはありません";
        if (CardLeftMediaArtist  != null) CardLeftMediaArtist.Text    = "";
        if (CardLeftMediaBgImage != null) CardLeftMediaBgImage.Source = null;
        if (CardLeftMediaIcon    != null) CardLeftMediaIcon.Source    = null;
        if (CardLeftMediaFill    != null) CardLeftMediaFill.Width     = 0;
        if (CardLeftMediaCurTime != null) CardLeftMediaCurTime.Text   = "0:00";
        if (CardLeftMediaTotTime != null) CardLeftMediaTotTime.Text   = "0:00";
        _cardLastMediaTitle = null;
        _cardLastMediaAumid = null;
    }

    /// <summary>セッションのタイムラインから経過バー・時間表示・再生アイコンを更新する。</summary>
    private void UpdateCardMediaTimeline(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession session, bool playing)
    {
        var playGeom = (Geometry)FindResource(playing ? "Bi.PauseFill" : "Bi.PlayFill");
        CardMediaPlayIcon.Data = playGeom;
        if (CardLeftMediaPlayIcon != null) CardLeftMediaPlayIcon.Data = playGeom;
        try
        {
            var tl = session.GetTimelineProperties();
            var duration = tl.EndTime - tl.StartTime;
            var pos      = tl.Position - tl.StartTime;
            if (playing)
            {
                var elapsed = DateTimeOffset.Now - tl.LastUpdatedTime;
                if (elapsed > TimeSpan.Zero) pos += elapsed;
            }
            if (duration <= TimeSpan.Zero)
            {
                CardMediaFill.Width = 0;
                CardMediaCurTime.Text = "0:00";
                CardMediaTotTime.Text = "0:00";
                if (CardLeftMediaFill    != null) CardLeftMediaFill.Width   = 0;
                if (CardLeftMediaCurTime != null) CardLeftMediaCurTime.Text = "0:00";
                if (CardLeftMediaTotTime != null) CardLeftMediaTotTime.Text = "0:00";
                return;
            }
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (pos > duration)      pos = duration;
            double frac = pos.TotalSeconds / duration.TotalSeconds;
            CardMediaFill.Width  = Math.Max(0, CardMediaTrack.ActualWidth * frac);
            CardMediaCurTime.Text = FormatMediaTime(pos);
            CardMediaTotTime.Text = FormatMediaTime(duration);
            if (CardLeftMediaFill != null && CardLeftMediaTrack != null)
                CardLeftMediaFill.Width = Math.Max(0, CardLeftMediaTrack.ActualWidth * frac);
            if (CardLeftMediaCurTime != null) CardLeftMediaCurTime.Text = FormatMediaTime(pos);
            if (CardLeftMediaTotTime != null) CardLeftMediaTotTime.Text = FormatMediaTime(duration);
        }
        catch
        {
            CardMediaFill.Width = 0;
            if (CardLeftMediaFill != null) CardLeftMediaFill.Width = 0;
        }
    }

    /// <summary>TimeSpan を m:ss 形式に整形する。</summary>
    private static string FormatMediaTime(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:D2}";

    /// <summary>サムネイルをぼかし背景用画像として設定する。</summary>
    private async Task UpdateCardMediaBackground(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties? props)
    {
        try
        {
            var thumbRef = props?.Thumbnail;
            if (thumbRef == null) { CardMediaBgImage.Source = null; return; }
            using var ras = await thumbRef.OpenReadAsync();
            using var net = ras.AsStreamForRead();
            var ms = new System.IO.MemoryStream();
            await net.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            CardMediaBgImage.Source = bmp;
            if (CardLeftMediaBgImage != null) CardLeftMediaBgImage.Source = bmp;
        }
        catch
        {
            CardMediaBgImage.Source = null;
            if (CardLeftMediaBgImage != null) CardLeftMediaBgImage.Source = null;
        }
    }

    /// <summary>再生中アプリのアイコンを取得して表示する（パッケージ→Win32 の順で解決）。</summary>
    private async Task UpdateCardAppIcon(string aumid)
    {
        var src = await TryGetPackagedAppLogo(aumid);
        src ??= TryGetWin32AppIcon(aumid);
        CardMediaAppIcon.Source = src;
        if (CardLeftMediaIcon != null) CardLeftMediaIcon.Source = src;
    }

    /// <summary>パッケージアプリのロゴを AppInfo 経由で取得する（失敗時は null）。</summary>
    private static async Task<System.Windows.Media.Imaging.BitmapSource?> TryGetPackagedAppLogo(string aumid)
    {
        try
        {
            if (string.IsNullOrEmpty(aumid)) return null;
            var appInfo = global::Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(aumid);
            var logoRef = appInfo.DisplayInfo.GetLogo(new global::Windows.Foundation.Size(32, 32));
            using var ras = await logoRef.OpenReadAsync();
            using var net = ras.AsStreamForRead();
            var ms = new System.IO.MemoryStream();
            await net.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Win32 アプリの実行ファイルからアイコンを抽出する（失敗時は null）。</summary>
    private static System.Windows.Media.Imaging.BitmapSource? TryGetWin32AppIcon(string aumid)
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
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DestroyIcon(large[0]); }
        }
        catch { return null; }
    }

    /// <summary>AppUserModelId から実行ファイルのフルパスを解決する。</summary>
    private static string? ResolveExecutablePath(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return null;
        if (aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(aumid))
            return aumid;
        var name = System.IO.Path.GetFileNameWithoutExtension(aumid);
        if (string.IsNullOrEmpty(name)) return null;
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) return path;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex,
        IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>経過バーのクリック位置に応じてシークする。</summary>
    private async void CardMediaTrack_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var session = _cardSmtc?.GetCurrentSession();
        if (session == null) return;
        try
        {
            var tl = session.GetTimelineProperties();
            var duration = tl.EndTime - tl.StartTime;
            if (duration <= TimeSpan.Zero) return;
            var track   = (sender as System.Windows.FrameworkElement) ?? CardMediaTrack;
            double x    = e.GetPosition(track).X;
            double w    = track.ActualWidth > 0 ? track.ActualWidth : CardMediaTrack.ActualWidth;
            double frac = Math.Clamp(x / w, 0, 1);
            var target  = tl.StartTime + TimeSpan.FromTicks((long)(duration.Ticks * frac));
            await session.TryChangePlaybackPositionAsync(target.Ticks);
            UpdateCardMediaTimeline(session, true);
        }
        catch { }
        e.Handled = true;
    }

    /// <summary>再生 / 一時停止を切り替える。</summary>
    private async void CardMediaPlayPause_Click(object sender, RoutedEventArgs e)
    {
        var session = _cardSmtc?.GetCurrentSession();
        if (session == null) return;
        try { await session.TryTogglePlayPauseAsync(); } catch { }
        await RefreshCardMedia();
    }

    /// <summary>前のトラックへスキップする。</summary>
    private async void CardMediaPrev_Click(object sender, RoutedEventArgs e)
    {
        var session = _cardSmtc?.GetCurrentSession();
        if (session == null) return;
        try { await session.TrySkipPreviousAsync(); } catch { }
        await RefreshCardMedia();
    }

    /// <summary>次のトラックへスキップする。</summary>
    private async void CardMediaNext_Click(object sender, RoutedEventArgs e)
    {
        var session = _cardSmtc?.GetCurrentSession();
        if (session == null) return;
        try { await session.TrySkipNextAsync(); } catch { }
        await RefreshCardMedia();
    }

    /// <summary>カードメディアのポーリングを停止する。</summary>
    private void StopCardMedia() => _cardMediaTimer?.Stop();

    /// <summary>HEX文字列を Color に変換する。失敗時は fallback を返す。</summary>
    private static Color ParseColorSafe(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    /// <summary>HEX文字列を SolidColorBrush に変換する。失敗時は fallback 色で返す。</summary>
    private static Brush ParseBrushSafe(string hex, Color fallback)
        => new SolidColorBrush(ParseColorSafe(hex, fallback));

    /// <summary>ツールボタンの Tag が示すビューへ遷移する。</summary>
    private void CardTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string view && !string.IsNullOrEmpty(view))
            _vm.NavigateToCommand.Execute(view);
    }

    private void CardGoToTodo_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("Todo");

    private void CardGoToTaskList_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("TaskList");

    // ── 入力ハンドラ（ホイール/ドラッグでカードを上下に流す）──────────
    /// <summary>マウスホイールでカードを1枚分、滑らかにめくる。</summary>
    private void CardCanvas_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        // 連続ホイールでも積み上げて目標位相を更新し、アニメーションで追従する
        _cardPhaseTarget += (e.Delta > 0 ? 1.0 : -1.0);
        StartCardAnimation();
        e.Handled = true;
    }

    /// <summary>左ボタン押下でドラッグ開始、マウスをキャプチャしてカーソルを変更する。</summary>
    private void CardCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        StopCardAnimation();
        _cardDragging = true;
        _cardDragLastPos = e.GetPosition(CardCanvas);
        CardCanvas.CaptureMouse();
        CardCanvas.Cursor = System.Windows.Input.Cursors.SizeNS;
        e.Handled = true;
    }

    /// <summary>ドラッグ中は縦移動量に比例して位相を連続更新し、カードを上下に流す。</summary>
    private void CardCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_cardDragging) return;
        var pos = e.GetPosition(CardCanvas);
        double dy = pos.Y - _cardDragLastPos.Y;
        _cardDragLastPos = pos;
        // 下へドラッグ(dy>0) = カードが下へ流れる = 未来日へ進む
        if (dy != 0)
        {
            _cardPhase += dy / CARD_DRAG_PX_PER_CARD;
            _cardPhaseTarget = _cardPhase;
            BuildCards();
        }
    }

    /// <summary>マウスアップでドラッグ終了し、位相を最寄りのカードへ滑らかにスナップする。</summary>
    private void CardCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_cardDragging) return;
        _cardDragging = false;
        CardCanvas.ReleaseMouseCapture();
        CardCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
        _cardPhaseTarget = Math.Round(_cardPhase);
        StartCardAnimation();
    }

    /// <summary>目標位相へ向けてカードを毎フレーム滑らかに補間するアニメーションを開始する。</summary>
    private void StartCardAnimation()
    {
        if (_cardAnimTimer == null)
        {
            _cardAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _cardAnimTimer.Tick += (_, _) =>
            {
                double diff = _cardPhaseTarget - _cardPhase;
                if (Math.Abs(diff) < 0.004)
                {
                    _cardPhase = _cardPhaseTarget;
                    BuildCards();
                    StopCardAnimation();
                    return;
                }
                _cardPhase += diff * 0.22;   // イージング（指数減衰）
                BuildCards();
            };
        }
        _cardAnimTimer.Start();
    }

    /// <summary>カードアニメーションを停止する。</summary>
    private void StopCardAnimation() => _cardAnimTimer?.Stop();
}

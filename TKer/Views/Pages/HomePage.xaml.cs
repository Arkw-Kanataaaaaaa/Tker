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
            if (CardClockText != null) CardClockText.Text = now;
        };
        _clockTimer.Start();

        Unloaded += (_, _) => { _clockTimer.Stop(); StopCardAnimation(); StopCardMedia(); };
        Loaded   += (_, _) => _clockTimer.Start();
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
        if (template == "Card")
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
        CardDateText.Text    = DateTime.Now.ToString("yyyy年MM月dd日 (ddd)");
        CardClockText.Text   = DateTime.Now.ToString("HH:mm:ss");

        BuildCardShortcuts();
        BuildCardCollections();
        BuildCardTodo();
        BuildCardTasks();
        _cardScheduleShownOffset = int.MinValue;   // 強制再構築
        BuildCards();
        StartCardMedia();
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

        // 右下の年月フォントサイズをカード領域の幅に追従させる
        double monthFont = Math.Clamp(w * 0.07, 36, 96);
        double yearFont  = monthFont * 0.55;
        CardMonthText.FontSize = monthFont;
        CardYearText.FontSize  = yearFont;
        CardMonthText.Margin   = new Thickness(0, -monthFont * 0.25, 0, 0);

        // 前面カードの選択日が変わったら、当日スケジュール・年月表示を更新する
        int selectedOffset = (int)Math.Round(_cardPhase);
        if (selectedOffset != _cardScheduleShownOffset)
        {
            _cardScheduleShownOffset = selectedOffset;
            var selDate = DateTime.Today.AddDays(selectedOffset);
            BuildCardSchedule(selDate);
            BuildCardMiniSchedule(selDate);
            CardYearText.Text  = selDate.ToString("yyyy");
            CardMonthText.Text = selDate.ToString("MM");
        }
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

        // 中央：日番号
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

        // 下段：アクセントバーのみ（年月はカード領域右下に集約表示するため非表示）
        var bottom = new Border
        {
            Height = 4 * us, Width = 56 * us, CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush(
                accent, Color.FromArgb(0x33, accent.R, accent.G, accent.B), 0),
        };
        Grid.SetRow(bottom, 2);
        grid.Children.Add(bottom);

        border.Child = grid;

        // スケール＋配置（原点基準スケールなので左上を中心から逆算）
        border.RenderTransform = new ScaleTransform(scale, scale);
        Canvas.SetLeft(border, cx - sw / 2);
        Canvas.SetTop(border,  cy - sh / 2);
        CardCanvas.Children.Add(border);
    }

    // ── ウィジェット部品ビルダー ─────────────────────────────
    /// <summary>共通カラー: 二次テキスト。</summary>
    private static Brush CardDimBrush => new SolidColorBrush(Color.FromRgb(0x9A, 0xA2, 0xB4));

    /// <summary>ショートカット部品を再構築する。</summary>
    private void BuildCardShortcuts()
    {
        CardShortcutPanel.Children.Clear();
        var shortcuts = _vm.AppSettingsService.Shortcuts;
        CardNoShortcutText.Visibility = shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var sc in shortcuts)
        {
            var path = sc.Path;
            var btn = new Button
            {
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(9, 5, 9, 5),
                Cursor  = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x44, 0x5A)),
                BorderThickness = new Thickness(1),
                ToolTip = path,
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = sc.Icon, FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            sp.Children.Add(new TextBlock { Text = sc.Name, FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
                VerticalAlignment = VerticalAlignment.Center });
            btn.Content = sp;
            btn.Click += (_, _) => OpenShortcut(path);
            CardShortcutPanel.Children.Add(btn);
        }
    }

    /// <summary>コレクションをグリッド（カバー画像 or アイコン）で表示する部品を再構築する。</summary>
    private void BuildCardCollections()
    {
        CardCollectionPanel.Children.Clear();
        var collections = _vm.CollectionService.Collections;
        CardNoCollectionText.Visibility = collections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var col in collections)
        {
            var card = new Border
            {
                Width = 100, Height = 116, Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x12, 0x18, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xA6, 0x6B, 0xFF)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ClipToBounds = true,
                ToolTip = col.Name,
            };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 表紙：Base64 画像があれば画像、なければアイコン絵文字
            var cover = new Border
            {
                CornerRadius = new CornerRadius(8), Margin = new Thickness(6, 6, 6, 2),
                Background = new SolidColorBrush(Color.FromArgb(0x55, 0x2A, 0x2A, 0x66)),
            };
            var img = DecodeBase64Image(col.CoverImageData);
            if (img != null)
            {
                cover.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            }
            else
            {
                cover.Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(col.Icon) ? "📁" : col.Icon,
                    FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            Grid.SetRow(cover, 0);
            g.Children.Add(cover);

            var nameTb = new TextBlock
            {
                Text = col.Name, FontSize = 11, FontWeight = FontWeights.Bold,
                Margin = new Thickness(7, 0, 7, 6), TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
            };
            Grid.SetRow(nameTb, 1);
            g.Children.Add(nameTb);

            card.Child = g;
            card.MouseLeftButtonUp += (_, _) => _vm.NavigateToCommand.Execute("Collection");
            CardCollectionPanel.Children.Add(card);
        }
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

    /// <summary>ToDo 部品を再構築する（未完了を優先表示）。</summary>
    private void BuildCardTodo()
    {
        CardTodoPanel.Children.Clear();
        var todos = _vm.TodoService.GetAll().Where(t => !t.IsCompleted).Take(6).ToList();
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
        var tasks = project.Tasks
            .Where(t => t.Status != "完了")
            .OrderByDescending(t => t.UpdatedAt)
            .Take(6)
            .ToList();
        if (tasks.Count == 0) tasks = project.Tasks.OrderByDescending(t => t.UpdatedAt).Take(6).ToList();

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

    /// <summary>カード領域右下の簡易予定カードを再構築する（最大3件＋件数）。</summary>
    private void BuildCardMiniSchedule(DateTime date)
    {
        if (CardMiniSchedulePanel == null) return;
        CardMiniSchedulePanel.Children.Clear();
        CardMiniScheduleTitle.Text = $"{date:M/d (ddd)} の予定";

        var entries = GetScheduleEntries(date);
        if (entries.Count == 0)
        {
            CardMiniSchedulePanel.Children.Add(new TextBlock
            {
                Text = "予定なし", FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA2, 0xB4)),
            });
            return;
        }

        foreach (var (time, label, color) in entries.Take(3))
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var t = new TextBlock
            {
                Text = time, FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(color),
            };
            Grid.SetColumn(t, 0);
            g.Children.Add(t);

            var l = new TextBlock
            {
                Text = label, FontSize = 12, Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
            };
            Grid.SetColumn(l, 1);
            g.Children.Add(l);

            CardMiniSchedulePanel.Children.Add(g);
        }

        if (entries.Count > 3)
        {
            CardMiniSchedulePanel.Children.Add(new TextBlock
            {
                Text = $"ほか {entries.Count - 3} 件", FontSize = 11, Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA2, 0xB4)),
            });
        }
    }

    /// <summary>指定日の予定（イベント＋該当タスク）を時刻順の (時刻, ラベル, 色) で返す。</summary>
    private List<(string time, string label, Color color)> GetScheduleEntries(DateTime date)
    {
        var rows = new List<(string time, string label, Color color)>();
        foreach (var ev in _vm.ScheduleService.GetByDate(date))
        {
            string time = ev.IsAllDay ? "終日" : ev.StartTime.ToString("HH:mm");
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

    /// <summary>選択日の時間単位スケジュール部品を再構築する。</summary>
    private void BuildCardSchedule(DateTime date)
    {
        if (CardSchedulePanel == null) return;
        CardSchedulePanel.Children.Clear();
        CardScheduleTitle.Text = $"{date:M月d日 (ddd)} の予定";

        // イベント（時間付き）とタスク（予定期間に該当）を時間順にまとめる
        var rows = new List<(string time, string label, Color color)>();

        foreach (var ev in _vm.ScheduleService.GetByDate(date))
        {
            string time = ev.IsAllDay ? "終日" : ev.StartTime.ToString("HH:mm");
            rows.Add((time, ev.Title, ParseColorSafe(ev.Color, Color.FromRgb(0x3D, 0x7E, 0xFF))));
        }

        var project = _vm.ProjectService.CurrentProject;
        if (project != null)
        {
            foreach (var t in project.Tasks.Where(t =>
                t.PlannedStartDate.HasValue && t.PlannedEndDate.HasValue &&
                t.PlannedStartDate.Value.Date <= date.Date && t.PlannedEndDate.Value.Date >= date.Date))
            {
                rows.Add(("予定", t.Name, Color.FromRgb(0x52, 0x9E, 0x72)));
            }
        }

        if (rows.Count == 0)
        {
            CardSchedulePanel.Children.Add(new TextBlock
            {
                Text = "この日の予定はありません", FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
                Foreground = CardDimBrush,
            });
            return;
        }

        foreach (var (time, label, color) in rows.OrderBy(r => r.time))
        {
            // 予定1件をミニカード（角丸チップ）として表示
            var chip = new Border
            {
                Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 9, 12, 9),
                Background = new SolidColorBrush(Color.FromArgb(0x4D, 0x12, 0x18, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bar = new Border
            {
                Width = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 1, 10, 1),
                Background = new SolidColorBrush(color),
            };
            Grid.SetColumn(bar, 0);
            g.Children.Add(bar);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = time, FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, color.R, color.G, color.B)),
            });
            sp.Children.Add(new TextBlock
            {
                Text = label, FontSize = 13, Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)),
            });
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);

            chip.Child = g;
            CardSchedulePanel.Children.Add(chip);
        }
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
        _cardLastMediaTitle = null;
        _cardLastMediaAumid = null;
    }

    /// <summary>セッションのタイムラインから経過バー・時間表示・再生アイコンを更新する。</summary>
    private void UpdateCardMediaTimeline(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession session, bool playing)
    {
        CardMediaPlayIcon.Data = (Geometry)FindResource(playing ? "Bi.PauseFill" : "Bi.PlayFill");
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
                return;
            }
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (pos > duration)      pos = duration;
            double frac = pos.TotalSeconds / duration.TotalSeconds;
            CardMediaFill.Width  = Math.Max(0, CardMediaTrack.ActualWidth * frac);
            CardMediaCurTime.Text = FormatMediaTime(pos);
            CardMediaTotTime.Text = FormatMediaTime(duration);
        }
        catch { CardMediaFill.Width = 0; }
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
        }
        catch { CardMediaBgImage.Source = null; }
    }

    /// <summary>再生中アプリのアイコンを取得して表示する（パッケージ→Win32 の順で解決）。</summary>
    private async Task UpdateCardAppIcon(string aumid)
    {
        var src = await TryGetPackagedAppLogo(aumid);
        src ??= TryGetWin32AppIcon(aumid);
        CardMediaAppIcon.Source = src;
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
            double x    = e.GetPosition(CardMediaTrack).X;
            double frac = Math.Clamp(x / CardMediaTrack.ActualWidth, 0, 1);
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

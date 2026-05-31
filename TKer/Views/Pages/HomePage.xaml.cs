using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        };
        _clockTimer.Start();

        Unloaded += (_, _) => _clockTimer.Stop();
        Loaded   += (_, _) => _clockTimer.Start();
    }

    /// <summary>ホーム画面の全コンポーネントを最新データで更新する。</summary>
    public void Refresh()
    {
        // ── テンプレート分岐：Planet スタイルなら専用ビューを表示して終了 ──
        if (_vm.AppSettingsService.HomeTemplate == "Planet")
        {
            HomeScroll.Visibility = Visibility.Collapsed;
            PlanetView.Visibility = Visibility.Visible;
            RefreshPlanetView();
            return;
        }
        HomeScroll.Visibility = Visibility.Visible;
        PlanetView.Visibility = Visibility.Collapsed;

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
    /// <summary>リング線の太さ（手前半周）。</summary>
    private const double PLANET_RING_STROKE_FRONT = 12.0;
    /// <summary>リング線の太さ（奥半周）。</summary>
    private const double PLANET_RING_STROKE_BACK = 8.0;
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

    /// <summary>傾いた楕円リングを Polyline で描画する。behindPlanet=true なら奥側半周のみ。</summary>
    private void AddRing(double cx, double cy, double rx, double ry, double tiltDeg, bool behindPlanet)
    {
        const int segs = 64;
        var pts = new System.Windows.Media.PointCollection();
        double tilt = tiltDeg * Math.PI / 180.0;
        double cosT = Math.Cos(tilt), sinT = Math.Sin(tilt);

        // 手前半周: φ ∈ [-π/2, π/2] (cos(φ) >= 0)
        // 奥半周  : φ ∈ [ π/2, 3π/2] (cos(φ) <  0)
        double phiStart = behindPlanet ?  Math.PI / 2 : -Math.PI / 2;
        double phiEnd   = behindPlanet ? 3 * Math.PI / 2 :  Math.PI / 2;

        for (int i = 0; i <= segs; i++)
        {
            double phi = phiStart + (phiEnd - phiStart) * i / segs;
            double lx = rx * Math.Sin(phi);
            double ly = ry * Math.Cos(phi);
            pts.Add(new Point(cx + lx * cosT - ly * sinT, cy + lx * sinT + ly * cosT));
        }
        if (pts.Count < 2) return;

        var ring = new System.Windows.Shapes.Polyline
        {
            Points = pts,
            Stroke = new LinearGradientBrush(
                Color.FromArgb(behindPlanet ? (byte)0x55 : (byte)0xCC, 0xCB, 0xB0, 0x82),
                Color.FromArgb(behindPlanet ? (byte)0x33 : (byte)0xAA, 0x88, 0x76, 0x55),
                0),
            StrokeThickness = behindPlanet ? PLANET_RING_STROKE_BACK : PLANET_RING_STROKE_FRONT,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
            Opacity = behindPlanet ? 0.6 : 1.0,
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
}

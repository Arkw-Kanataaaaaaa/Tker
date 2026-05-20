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
            if (ClockText != null)
                ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
        };
        _clockTimer.Start();

        Unloaded += (_, _) => _clockTimer.Stop();
        Loaded   += (_, _) => _clockTimer.Start();
    }

    /// <summary>ホーム画面の全コンポーネントを最新データで更新する。</summary>
    public void Refresh()
    {
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
}

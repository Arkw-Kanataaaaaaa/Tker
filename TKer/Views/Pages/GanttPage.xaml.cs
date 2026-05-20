using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TKer.Models;
using TKer.ViewModels;

namespace TKer.Views.Pages;

public partial class GanttPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;
    private DateTime _viewStart;
    private int      _viewDays = 60;
    private bool     _hideCompleted = false;   // ⑭
    private bool     _isSyncing     = false;   // ③ スクロール無限ループ防止

    private const double ColW  = 32;
    private const double RowH  = 38;
    private const double PlanY = 8;
    private const double ActY  = 22;
    private const double BarH  = 12;

    private static readonly SolidColorBrush PlanBrush    = new(Color.FromArgb(180, 61, 126, 255));
    private static readonly SolidColorBrush ActBrush     = new(Color.FromArgb(180, 0, 230, 118));
    private static readonly SolidColorBrush TodayBrush   = new(Color.FromArgb(200, 0, 212, 255));
    private static readonly SolidColorBrush CatBrush     = new(Color.FromArgb(30, 61, 126, 255));
    private static readonly SolidColorBrush GridBrush    = new(Color.FromArgb(60, 37, 45, 64));
    private static readonly SolidColorBrush WeekendBrush = new(Color.FromArgb(20, 255, 255, 255));

    public GanttPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        _viewStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    }

    public void Refresh() => RenderGantt();

    private void RenderGantt()
    {
        if (_vm.ProjectService.CurrentProject == null) return;

        MonthLabel.Text = _viewStart.ToString("yyyy年 M月");
        var rows = _vm.ProjectService.GetGanttRows(hideCompleted: _hideCompleted);
        if (rows.Count == 0) return;


        // ⑨ パフォーマンス警告（タスク数が多い場合）
        if (rows.Count > 200)
        {
            var proceed = System.Windows.MessageBox.Show(
                $"タスクが{rows.Count}件あります。描画に時間がかかる場合があります。続行しますか？",
                "パフォーマンス警告",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (proceed != System.Windows.MessageBoxResult.Yes) return;
        }
        bool performanceWarning = rows.Count > 200; // suppress re-check

        double totalW = ColW * _viewDays;
        double totalH = RowH * rows.Count;

        GanttCanvas.Width  = totalW;
        GanttCanvas.Height = totalH;
        GanttCanvas.Children.Clear();
        DateHeaderCanvas.Width = totalW;
        DateHeaderCanvas.Children.Clear();

        // ③ LabelColumn をスタックパネルで再構築（ScrollViewer の縦スクロールと同期）
        LabelColumn.Children.Clear();

        // ── 日付ヘッダー ──
        string? lastMonth = null;
        for (int i = 0; i < _viewDays; i++)
        {
            var d        = _viewStart.AddDays(i);
            double x     = i * ColW;
            bool isWeekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            bool isToday   = d.Date == DateTime.Today;

            if (d.ToString("M月") != lastMonth)
            {
                lastMonth = d.ToString("M月");
                var ml = new TextBlock
                {
                    Text       = d.ToString("M月"),
                    Foreground = new SolidColorBrush(Color.FromRgb(136, 146, 170)),
                    FontSize   = 10,
                    FontFamily = new FontFamily("Consolas")
                };
                Canvas.SetLeft(ml, x + 2);
                Canvas.SetTop(ml, 2);
                DateHeaderCanvas.Children.Add(ml);
            }

            var tb = new TextBlock
            {
                Text      = d.Day.ToString(),
                Foreground = isToday
                    ? TodayBrush
                    : isWeekend
                        ? new SolidColorBrush(Color.FromRgb(100, 120, 160))
                        : new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                FontSize  = 9,
                Width     = ColW,
                TextAlignment = System.Windows.TextAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, 22);
            DateHeaderCanvas.Children.Add(tb);

            if (i > 0)
            {
                var line = new Line
                {
                    X1 = x, Y1 = 0, X2 = x, Y2 = totalH,
                    Stroke = GridBrush, StrokeThickness = 1
                };
                GanttCanvas.Children.Add(line);
            }

            if (isWeekend)
            {
                var rect = new Rectangle { Width = ColW, Height = totalH, Fill = WeekendBrush };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, 0);
                GanttCanvas.Children.Add(rect);
            }
        }

        // 今日ライン
        double todayX = (DateTime.Today.Date - _viewStart.Date).TotalDays * ColW;
        if (todayX >= 0 && todayX <= totalW)
        {
            var todayLine = new Rectangle
            {
                Width  = 2,
                Height = totalH,
                Fill   = TodayBrush,
                Opacity = 0.8
            };
            Canvas.SetLeft(todayLine, todayX);
            Canvas.SetTop(todayLine, 0);
            Panel.SetZIndex(todayLine, 10);
            GanttCanvas.Children.Add(todayLine);
        }

        // ── 行描画 ──
        for (int ri = 0; ri < rows.Count; ri++)
        {
            var row    = rows[ri];
            double rowY = ri * RowH;

            // ③ ラベル列をStackPanelに直接追加（スクロール同期）
            var labelBorder = new Border
            {
                Height           = RowH,
                BorderBrush      = GridBrush,
                BorderThickness  = new Thickness(0, 0, 0, 1),
                Background       = row.IsCategory ? CatBrush : Brushes.Transparent,
                Padding          = new Thickness(row.IndentLevel == 0 ? 12 : row.IndentLevel == 1 ? 28 : 44, 0, 8, 0)
            };
            var labelText = new TextBlock
            {
                Text       = row.Label,
                Foreground = row.IsCategory
                    ? new SolidColorBrush(Color.FromRgb(232, 234, 240))
                    : new SolidColorBrush(Color.FromRgb(136, 146, 170)),
                FontSize   = row.IsCategory ? 13 : 12,
                FontWeight = row.IsCategory ? FontWeights.Bold : FontWeights.Normal,
                VerticalAlignment  = VerticalAlignment.Center,
                TextTrimming       = TextTrimming.CharacterEllipsis
            };
            labelBorder.Child = labelText;
            LabelColumn.Children.Add(labelBorder);

            // 行区切り線
            GanttCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = rowY + RowH - 0.5, X2 = totalW, Y2 = rowY + RowH - 0.5,
                Stroke = GridBrush, StrokeThickness = 1
            });

            if (row.IsCategory) continue;

            DrawBar(row.PlannedStart, row.PlannedEnd, rowY + PlanY, BarH, PlanBrush,
                $"予定: {row.PlannedStart:MM/dd}〜{row.PlannedEnd:MM/dd}");
            DrawBar(row.ActualStart, row.ActualEnd, rowY + ActY, BarH, ActBrush,
                $"実績: {row.ActualStart:MM/dd}〜{row.ActualEnd:MM/dd}");
        }
    }

    private void DrawBar(DateTime? start, DateTime? end, double y, double h,
                         SolidColorBrush brush, string tooltip)
    {
        if (!start.HasValue || !end.HasValue) return;
        double totalW  = ColW * _viewDays;
        double x       = (start.Value.Date - _viewStart.Date).TotalDays * ColW;
        double w       = Math.Max(((end.Value.Date - start.Value.Date).TotalDays + 1) * ColW, ColW * 0.5);
        if (x + w < 0 || x > totalW) return;

        double clippedX = Math.Max(x, 0);
        double clippedW = Math.Min(x + w, totalW) - clippedX;

        var rect = new Rectangle
        {
            Width = clippedW, Height = h,
            Fill = brush, RadiusX = 3, RadiusY = 3
        };
        ToolTipService.SetToolTip(rect, tooltip);
        Canvas.SetLeft(rect, clippedX);
        Canvas.SetTop(rect, y);
        GanttCanvas.Children.Add(rect);
    }

    // ③ BarScroll 横スクロール → HeaderScroll と同期
    private void BarScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        HeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
    }

    // ③ BodyVScroll 縦スクロール → LabelScroll と同期
    private void BodyVScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncing) return;
        _isSyncing = true;
        LabelScroll.ScrollToVerticalOffset(e.VerticalOffset);
        _isSyncing = false;
    }

    private void LabelScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncing) return;
        _isSyncing = true;
        BodyVScroll.ScrollToVerticalOffset(e.VerticalOffset);
        _isSyncing = false;
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = _viewStart.AddMonths(-1);
        _viewDays  = DateTime.DaysInMonth(_viewStart.Year, _viewStart.Month) + 14;
        RenderGantt();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = _viewStart.AddMonths(1);
        _viewDays  = DateTime.DaysInMonth(_viewStart.Year, _viewStart.Month) + 14;
        RenderGantt();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _viewDays  = 60;
        RenderGantt();
        double todayX = (DateTime.Today - _viewStart.Date).TotalDays * ColW;
        BarScroll.ScrollToHorizontalOffset(Math.Max(todayX - 100, 0));
    }

    // ⑫ 全体表示: タスク最小日〜最大日
    private void FitAll_Click(object sender, RoutedEventArgs e)
    {
        var (min, max) = _vm.ProjectService.GetTaskDateRange();
        _viewStart = new DateTime(min.Year, min.Month, 1);
        _viewDays  = (int)(max - _viewStart).TotalDays + 7;
        RenderGantt();
    }

    // ⑭ 完了タスク表示トグル
    private void ToggleCompleted_Click(object sender, RoutedEventArgs e)
    {
        _hideCompleted = !_hideCompleted;
        ToggleCompletedBtn.Content = _hideCompleted ? "完了を表示" : "完了を非表示";
        RenderGantt();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RenderGantt();
}

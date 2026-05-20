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

/// <summary>プロジェクトタスクのガントチャートを表示するページ。</summary>
public partial class GanttPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;
    private DateTime _viewStart;
    private int      _viewDays = 60;
    private bool     _hideCompleted = false;   // ⑭
    private bool     _isSyncing     = false;   // ③ スクロール無限ループ防止

    private const double COL_W  = 32;
    private const double ROW_H  = 38;
    private const double PLAN_Y = 8;
    private const double ACT_Y  = 22;
    private const double BAR_H  = 12;

    private static readonly SolidColorBrush PLAN_BRUSH    = new(Color.FromArgb(180, 61, 126, 255));
    private static readonly SolidColorBrush ACT_BRUSH     = new(Color.FromArgb(180, 0, 230, 118));
    private static readonly SolidColorBrush TODAY_BRUSH   = new(Color.FromArgb(200, 0, 212, 255));
    private static readonly SolidColorBrush CAT_BRUSH     = new(Color.FromArgb(30, 61, 126, 255));
    private static readonly SolidColorBrush GRID_BRUSH    = new(Color.FromArgb(60, 37, 45, 64));
    private static readonly SolidColorBrush WEEKEND_BRUSH = new(Color.FromArgb(20, 255, 255, 255));

    /// <summary>ガントページを初期化し、表示開始日を設定する。</summary>
    public GanttPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        _viewStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    }

    /// <summary>ガントチャートを再描画する。</summary>
    public void Refresh() => RenderGantt();

    /// <summary>現在の表示範囲でガントチャート全体を描画する。</summary>
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

        double totalW = COL_W * _viewDays;
        double totalH = ROW_H * rows.Count;

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
            double x     = i * COL_W;
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
                    ? TODAY_BRUSH
                    : isWeekend
                        ? new SolidColorBrush(Color.FromRgb(100, 120, 160))
                        : new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                FontSize  = 9,
                Width     = COL_W,
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
                    Stroke = GRID_BRUSH, StrokeThickness = 1
                };
                GanttCanvas.Children.Add(line);
            }

            if (isWeekend)
            {
                var rect = new Rectangle { Width = COL_W, Height = totalH, Fill = WEEKEND_BRUSH };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, 0);
                GanttCanvas.Children.Add(rect);
            }
        }

        // 今日ライン
        double todayX = (DateTime.Today.Date - _viewStart.Date).TotalDays * COL_W;
        if (todayX >= 0 && todayX <= totalW)
        {
            var todayLine = new Rectangle
            {
                Width  = 2,
                Height = totalH,
                Fill   = TODAY_BRUSH,
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
            double rowY = ri * ROW_H;

            // ③ ラベル列をStackPanelに直接追加（スクロール同期）
            var labelBorder = new Border
            {
                Height           = ROW_H,
                BorderBrush      = GRID_BRUSH,
                BorderThickness  = new Thickness(0, 0, 0, 1),
                Background       = row.IsCategory ? CAT_BRUSH : Brushes.Transparent,
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
                X1 = 0, Y1 = rowY + ROW_H - 0.5, X2 = totalW, Y2 = rowY + ROW_H - 0.5,
                Stroke = GRID_BRUSH, StrokeThickness = 1
            });

            if (row.IsCategory) continue;

            DrawBar(row.PlannedStart, row.PlannedEnd, rowY + PLAN_Y, BAR_H, PLAN_BRUSH,
                $"予定: {row.PlannedStart:MM/dd}〜{row.PlannedEnd:MM/dd}");
            DrawBar(row.ActualStart, row.ActualEnd, rowY + ACT_Y, BAR_H, ACT_BRUSH,
                $"実績: {row.ActualStart:MM/dd}〜{row.ActualEnd:MM/dd}");
        }
    }

    /// <summary>指定期間のガントバーをキャンバスに描画する。</summary>
    private void DrawBar(DateTime? start, DateTime? end, double y, double h,
                         SolidColorBrush brush, string tooltip)
    {
        if (!start.HasValue || !end.HasValue) return;
        double totalW  = COL_W * _viewDays;
        double x       = (start.Value.Date - _viewStart.Date).TotalDays * COL_W;
        double w       = Math.Max(((end.Value.Date - start.Value.Date).TotalDays + 1) * COL_W, COL_W * 0.5);
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

    /// <summary>横スクロール変更時にヘッダーを同期する。</summary>
    private void BarScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        HeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
    }

    /// <summary>縦スクロール変更時にラベル列を同期する。</summary>
    private void BodyVScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncing) return;
        _isSyncing = true;
        LabelScroll.ScrollToVerticalOffset(e.VerticalOffset);
        _isSyncing = false;
    }

    /// <summary>ラベル列縦スクロール変更時にボディを同期する。</summary>
    private void LabelScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncing) return;
        _isSyncing = true;
        BodyVScroll.ScrollToVerticalOffset(e.VerticalOffset);
        _isSyncing = false;
    }

    /// <summary>前月に移動してガントを再描画する。</summary>
    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = _viewStart.AddMonths(-1);
        _viewDays  = DateTime.DaysInMonth(_viewStart.Year, _viewStart.Month) + 14;
        RenderGantt();
    }

    /// <summary>翌月に移動してガントを再描画する。</summary>
    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = _viewStart.AddMonths(1);
        _viewDays  = DateTime.DaysInMonth(_viewStart.Year, _viewStart.Month) + 14;
        RenderGantt();
    }

    /// <summary>今日の日付を含む月に戻ってガントを再描画する。</summary>
    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _viewDays  = 60;
        RenderGantt();
        double todayX = (DateTime.Today - _viewStart.Date).TotalDays * COL_W;
        BarScroll.ScrollToHorizontalOffset(Math.Max(todayX - 100, 0));
    }

    /// <summary>全タスクの日付範囲に合わせて表示範囲を調整する。</summary>
    private void FitAll_Click(object sender, RoutedEventArgs e)
    {
        var (min, max) = _vm.ProjectService.GetTaskDateRange();
        _viewStart = new DateTime(min.Year, min.Month, 1);
        _viewDays  = (int)(max - _viewStart).TotalDays + 7;
        RenderGantt();
    }

    /// <summary>完了タスクの表示・非表示を切り替えてガントを再描画する。</summary>
    private void ToggleCompleted_Click(object sender, RoutedEventArgs e)
    {
        _hideCompleted = !_hideCompleted;
        ToggleCompletedBtn.Content = _hideCompleted ? "完了を表示" : "完了を非表示";
        RenderGantt();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RenderGantt();
}

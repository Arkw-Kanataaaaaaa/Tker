using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;

namespace TKer.Views.Pages;


/// <summary>月別カレンダーと日別イベントを表示するページ。</summary>
public partial class CalendarPage : Page, IRefreshable
{
    private readonly MainViewModel  _vm;
    private readonly ScheduleService _svc;

    private int      _year;
    private int      _month;
    private DateTime _selectedDate = DateTime.Today;
    private bool     _hideCompleted = false;

    // 選択枠のみを再スタイルするための日付→セル対応表（全再描画を避ける）
    private readonly Dictionary<DateTime, Border> _cellBorders = new();
    private DateTime _gridStart;
    private Window?  _keyDownWindow;

    /// <summary>ページを初期化し、スケジュールサービスとイベントを設定する。</summary>
    public CalendarPage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = vm.ScheduleService;
        InitializeComponent();
        _year  = DateTime.Today.Year;
        _month = DateTime.Today.Month;
        _svc.DataChanged += OnDataChanged;
        Loaded += (_, _) =>
        {
            Refresh();
            if (Window.GetWindow(this) is { } win)
            {
                win.KeyDown -= Window_KeyDown;
                win.KeyDown += Window_KeyDown;
                _keyDownWindow = win;
            }
        };
        Unloaded += (_, _) =>
        {
            _svc.DataChanged -= OnDataChanged;
            if (_keyDownWindow is { } win) win.KeyDown -= Window_KeyDown;
            _keyDownWindow = null;
        };
    }

    private void OnDataChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RenderCalendar);

    /// <summary>指定した日付へナビゲートし、年月を更新する。</summary>
    public void NavigateTo(DateTime date)
    {
        _selectedDate = date.Date;
        _year  = date.Year;
        _month = date.Month;
    }

    /// <summary>テーマを適用してカレンダーと日別イベントを再描画する。</summary>
    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Cal_Header"));
        MonthLabel.Text = $"{_year}年 {_month}月";
        RenderCalendar();
        ShowDayEvents(_selectedDate);
    }

    // ── カレンダー描画 ────────────────────────────────────
    /// <summary>カレンダーグリッド全体を再描画する。</summary>
    private void RenderCalendar()
    {
        MonthLabel.Text = $"{_year}年 {_month}月";
        CalGrid.Children.Clear();
        _cellBorders.Clear();

        var scheduleEvents = _svc.GetByMonth(_year, _month);
        IReadOnlyList<TaskItem> allTasks =
            _vm.ProjectService.CurrentProject?.Tasks ?? new List<TaskItem>();

        var projectCells = _vm.ProjectService.GetCalendarCells(_year, _month, _hideCompleted);
        // date → cell の辞書
        var cellMap = projectCells.ToDictionary(c => c.Date.Date);

        var first     = new DateTime(_year, _month, 1);
        int startDow  = (int)first.DayOfWeek;
        int daysInMon = DateTime.DaysInMonth(_year, _month);
        int daysInPrev = DateTime.DaysInMonth(
            _month == 1 ? _year - 1 : _year,
            _month == 1 ? 12 : _month - 1);
        int prevYear  = _month == 1 ? _year - 1 : _year;
        int prevMonth = _month == 1 ? 12 : _month - 1;
        int nextYear  = _month == 12 ? _year + 1 : _year;
        int nextMonth = _month == 12 ? 1 : _month + 1;

        _gridStart = first.AddDays(-startDow);

        // 前月埋め
        for (int i = 0; i < startDow; i++)
        {
            int d = daysInPrev - startDow + 1 + i;
            CalGrid.Children.Add(BuildDayCell(
                new DateTime(prevYear, prevMonth, d), false, scheduleEvents, allTasks, cellMap));
        }
        // 今月
        for (int d = 1; d <= daysInMon; d++)
        {
            var date = new DateTime(_year, _month, d);
            CalGrid.Children.Add(BuildDayCell(date, true, scheduleEvents, allTasks, cellMap));
        }
        // 翌月埋め（UniformGrid の 6 行に合わせ常に 42 セル描画して下段の空きをなくす）
        int total = startDow + daysInMon;
        const int rows = 42;
        for (int i = total + 1; i <= rows; i++)
        {
            int d = i - total;
            CalGrid.Children.Add(BuildDayCell(
                new DateTime(nextYear, nextMonth, d), false, scheduleEvents, allTasks, cellMap));
        }
    }

    /// <summary>1日分のカレンダーセルを生成して返す。</summary>
    private Border BuildDayCell(DateTime date, bool isCurrent,
        IReadOnlyList<ScheduleEvent> scheduleEvents,
        IReadOnlyList<TaskItem> allTasks,
        Dictionary<DateTime, CalendarCell> cellMap)
    {
        bool isToday    = date.Date == DateTime.Today;
        bool isHoliday  = JapaneseHolidays.IsHoliday(date);
        string? holidayName = JapaneseHolidays.GetName(date);
        bool isSunday   = date.DayOfWeek == DayOfWeek.Sunday;
        bool isSaturday = date.DayOfWeek == DayOfWeek.Saturday;

        // 背景色
        Color bgColor;
        if (isToday)
            bgColor = Color.FromArgb(40, 0x23, 0x83, 0xE2);
        else if (isHoliday)
            bgColor = Color.FromRgb(55, 47, 47);
        else
            bgColor = Color.FromRgb(47, 47, 47);

        var border = new Border
        {
            Margin          = new Thickness(2),
            Background      = new SolidColorBrush(bgColor),
            CornerRadius    = new CornerRadius(6),
            MinHeight       = 80,
            Padding         = new Thickness(5),
            Opacity         = isCurrent ? 1.0 : 0.35,
            Cursor          = Cursors.Hand
        };
        StyleCellBorder(border, date);   // 選択枠・今日枠の色／太さを適用

        var sp = new StackPanel();

        // 日付ラベル
        Color dayFg;
        if (isToday)
            dayFg = Color.FromRgb(35, 131, 226);
        else if (!isCurrent)
            dayFg = Color.FromRgb(100, 100, 100);
        else if (isSunday || isHoliday)
            dayFg = Color.FromRgb(0xEF, 0x53, 0x50);
        else if (isSaturday)
            dayFg = Color.FromRgb(0x42, 0xA5, 0xF5);
        else
            dayFg = Color.FromRgb(207, 207, 207);

        sp.Children.Add(new TextBlock
        {
            Text       = date.Day.ToString(),
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 12,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
            Foreground = new SolidColorBrush(dayFg),
            Margin     = new Thickness(0, 0, 0, 2)
        });

        // 祝日名
        if (isHoliday && holidayName != null)
        {
            sp.Children.Add(new TextBlock
            {
                Text          = holidayName,
                FontSize      = 9,
                Foreground    = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
                Margin        = new Thickness(0, 0, 0, 2),
                TextTrimming  = TextTrimming.CharacterEllipsis
            });
        }

        // スケジュールイベントチップ（橙系）
        var dayEvents = scheduleEvents
            .Where(ev => ev.StartTime.Date <= date.Date && ev.EndTime.Date >= date.Date)
            .Take(2);
        foreach (var ev in dayEvents)
        {
            Color c;
            try { c = (Color)ColorConverter.ConvertFromString(ev.Color); }
            catch { c = Color.FromRgb(0xFF, 0x70, 0x43); }

            sp.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                Background   = new SolidColorBrush(Color.FromArgb(200, c.R, c.G, c.B)),
                Margin       = new Thickness(0, 1, 0, 1),
                Padding      = new Thickness(3, 1, 3, 1),
                Child        = new TextBlock
                {
                    Text         = ev.Title,
                    FontSize     = 10,
                    Foreground   = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            });
        }

        // プロジェクトタスクチップ（cellMap から取得）
        if (cellMap.TryGetValue(date.Date, out var cell))
        {
            foreach (var t in cell.PlannedTasks.Take(2))
            {
                var chip = new Border
                {
                    Background   = new SolidColorBrush(Color.FromArgb(50, 35, 131, 226)),
                    CornerRadius = new CornerRadius(3),
                    Padding      = new Thickness(3, 1, 3, 1),
                    Margin       = new Thickness(0, 1, 0, 1)
                };
                chip.Child = new TextBlock
                {
                    Text         = t.Name,
                    FontSize     = 10,
                    Foreground   = new SolidColorBrush(Color.FromRgb(35, 131, 226)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                ToolTipService.SetToolTip(chip,
                    $"[予定] {t.Name}\n担当: {t.Assignee}\n{t.PlannedStartDate:M/d}～{t.PlannedEndDate:M/d}");
                sp.Children.Add(chip);
            }
            foreach (var t in cell.ActualTasks.Take(1))
            {
                var chip = new Border
                {
                    Background   = new SolidColorBrush(Color.FromArgb(40, 82, 158, 114)),
                    CornerRadius = new CornerRadius(3),
                    Padding      = new Thickness(3, 1, 3, 1),
                    Margin       = new Thickness(0, 1, 0, 1)
                };
                chip.Child = new TextBlock
                {
                    Text         = t.Name,
                    FontSize     = 10,
                    Foreground   = new SolidColorBrush(Color.FromRgb(82, 158, 114)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                ToolTipService.SetToolTip(chip,
                    $"[実績] {t.Name}\n{t.ActualStartDate:M/d}～{t.ActualEndDate:M/d}");
                sp.Children.Add(chip);
            }
        }

        // オーバーフロー表示（イベントのみの日でも算出するため cellMap ブロック外で計算）
        int plannedCount = cell?.PlannedTasks.Count ?? 0;
        int actualCount  = cell?.ActualTasks.Count  ?? 0;
        int eventCount   = scheduleEvents.Count(ev => ev.StartTime.Date <= date.Date && ev.EndTime.Date >= date.Date);
        int totalItems   = plannedCount + actualCount + eventCount;
        int shown        = Math.Min(2, plannedCount) + Math.Min(1, actualCount) + Math.Min(2, eventCount);
        int overflow     = totalItems - shown;
        if (overflow > 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text       = $"+{overflow}件",
                FontSize   = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 119, 116))
            });
        }

        border.Child = sp;
        border.MouseLeftButtonUp += (_, _) => SelectDate(date);
        _cellBorders[date.Date] = border;
        return border;
    }

    /// <summary>選択状態・今日・祝日に応じてセルの枠線色と太さを設定する。</summary>
    private void StyleCellBorder(Border border, DateTime date)
    {
        bool isToday    = date.Date == DateTime.Today;
        bool isSelected = date.Date == _selectedDate.Date;
        bool isHoliday  = JapaneseHolidays.IsHoliday(date);

        Color borderColor;
        if (isSelected)
            borderColor = Color.FromRgb(0x00, 0xBF, 0xD8); // AccentCyan
        else if (isToday)
            borderColor = Color.FromRgb(35, 131, 226);
        else if (isHoliday)
            borderColor = Color.FromRgb(100, 55, 55);
        else
            borderColor = Color.FromRgb(55, 55, 55);

        border.BorderBrush     = new SolidColorBrush(borderColor);
        border.BorderThickness = (isSelected || isToday) ? new Thickness(2) : new Thickness(1);
    }

    /// <summary>指定日を選択する。表示中グリッド内なら該当セルのみ再スタイルし、範囲外なら月を移動する。</summary>
    private void SelectDate(DateTime date)
    {
        date = date.Date;
        if (date == _selectedDate.Date)
        {
            ShowDayEvents(date);
            return;
        }

        bool inGrid = date >= _gridStart && date <= _gridStart.AddDays(41);
        if (inGrid && _cellBorders.TryGetValue(date, out var newBorder))
        {
            var prev = _selectedDate.Date;
            _selectedDate = date;
            if (_cellBorders.TryGetValue(prev, out var oldBorder))
                StyleCellBorder(oldBorder, prev);   // 旧選択セルを通常表示に戻す
            StyleCellBorder(newBorder, date);
            ShowDayEvents(date);
        }
        else
        {
            _selectedDate = date;
            _year  = date.Year;
            _month = date.Month;
            Refresh();
        }
    }

    // ── 日別詳細パネル ────────────────────────────────────
    /// <summary>選択日のイベントとタスクを詳細パネルに表示する。</summary>
    private void ShowDayEvents(DateTime date)
    {
        TxtSelectedDate.Text = $"{date:M月d日 (ddd)}";
        EventListPanel.Children.Clear();

        // スケジュールイベント
        var events = _svc.GetByDate(date);
        if (!events.Any())
        {
            EventListPanel.Children.Add(new TextBlock
            {
                Text       = "イベントなし",
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                Margin     = new Thickness(8, 16, 8, 0),
                FontSize   = 12
            });
        }
        foreach (var ev in events)
            EventListPanel.Children.Add(BuildEventCard(ev));

        // プロジェクトタスク（予定・実績いずれかが当日に掛かるもの）
        var tasks = (_vm.ProjectService.CurrentProject?.Tasks ?? new List<TaskItem>())
            .Where(t =>
                (t.PlannedStartDate?.Date <= date.Date && t.PlannedEndDate?.Date >= date.Date) ||
                (t.ActualStartDate?.Date  <= date.Date && t.ActualEndDate?.Date  >= date.Date))
            .ToList();
        if (tasks.Any())
        {
            EventListPanel.Children.Add(new TextBlock
            {
                Text       = "プロジェクトタスク",
                FontSize   = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                Margin     = new Thickness(4, 16, 4, 4)
            });
            foreach (var t in tasks)
                EventListPanel.Children.Add(BuildTaskChip(t, date));
        }
    }

    /// <summary>スケジュールイベントのカードUIを生成して返す。</summary>
    private Border BuildEventCard(ScheduleEvent ev)
    {
        Color c;
        try { c = (Color)ColorConverter.ConvertFromString(ev.Color); }
        catch { c = Color.FromRgb(0x23, 0x83, 0xE2); }

        string timeText = ev.IsAllDay ? "終日" : $"{ev.StartTime:HH:mm} 〜 {ev.EndTime:HH:mm}";

        var card = new Border
        {
            Margin          = new Thickness(4, 4, 4, 0),
            Padding         = new Thickness(10, 8, 10, 8),
            CornerRadius    = new CornerRadius(6),
            Background      = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
            BorderBrush     = new SolidColorBrush(c),
            BorderThickness = new Thickness(3, 0, 0, 0)
        };

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text       = ev.Title,
            FontSize   = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(207, 207, 207))
        });
        sp.Children.Add(new TextBlock
        {
            Text       = timeText,
            FontSize   = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            Margin     = new Thickness(0, 2, 0, 0)
        });

        if (!string.IsNullOrEmpty(ev.Description))
            sp.Children.Add(new TextBlock
            {
                Text        = ev.Description,
                FontSize    = 11,
                Foreground  = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                Margin      = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

        if (!string.IsNullOrEmpty(ev.Location))
            sp.Children.Add(new TextBlock
            {
                Text       = $"📍 {ev.Location}",
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                Margin     = new Thickness(0, 2, 0, 0)
            });

        // 編集・削除ボタン
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        var btnEdit = new Button
        {
            Content = "編集", FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            Margin  = new Thickness(0, 0, 6, 0)
        };
        if (FindResource("SecondaryButton") is Style secStyle)
            btnEdit.Style = secStyle;
        btnEdit.Click += (_, _) => EditEvent(ev);

        var btnDel = new Button
        {
            Content    = "削除",
            FontSize   = 11,
            Padding    = new Thickness(8, 3, 8, 3),
            Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50))
        };
        if (FindResource("SecondaryButton") is Style secStyle2)
            btnDel.Style = secStyle2;
        btnDel.Click += (_, _) => DeleteEvent(ev);

        btnPanel.Children.Add(btnEdit);
        btnPanel.Children.Add(btnDel);
        sp.Children.Add(btnPanel);

        card.Child = sp;
        return card;
    }

    /// <summary>指定日に掛かるプロジェクトタスクのチップUIを生成して返す。</summary>
    private Border BuildTaskChip(TaskItem t, DateTime date)
    {
        bool inPlanned = t.PlannedStartDate?.Date <= date.Date && t.PlannedEndDate?.Date >= date.Date;
        bool inActual  = t.ActualStartDate?.Date  <= date.Date && t.ActualEndDate?.Date  >= date.Date;

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text       = t.Name,
            FontSize   = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(207, 207, 207))
        });
        if (inPlanned)
            sp.Children.Add(new TextBlock
            {
                Text       = $"予定 {t.PlannedStartDate:MM/dd} 〜 {t.PlannedEndDate:MM/dd}",
                FontSize   = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 131, 226)),
                Margin     = new Thickness(0, 2, 0, 0)
            });
        if (inActual)
            sp.Children.Add(new TextBlock
            {
                Text       = $"実績 {t.ActualStartDate:MM/dd} 〜 {t.ActualEndDate:MM/dd}",
                FontSize   = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(82, 158, 114)),
                Margin     = new Thickness(0, 2, 0, 0)
            });
        sp.Children.Add(new TextBlock
        {
            Text       = $"[{t.Status}]",
            FontSize   = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            Margin     = new Thickness(0, 2, 0, 0)
        });

        // 実績が当日に掛かるなら緑、予定のみなら紫系のラインで区別する
        Color barColor = inActual
            ? Color.FromRgb(0x52, 0x9E, 0x72)
            : Color.FromArgb(120, 0x66, 0x66, 0x99);

        return new Border
        {
            Margin          = new Thickness(4, 2, 4, 0),
            Padding         = new Thickness(10, 6, 10, 6),
            CornerRadius    = new CornerRadius(6),
            Background      = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
            BorderBrush     = new SolidColorBrush(barColor),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Child           = sp
        };
    }

    // ── イベント追加・編集・削除 ──────────────────────────
    private void BtnAddEvent_Click(object sender, RoutedEventArgs e) => OpenEventDialog(null);

    private void EditEvent(ScheduleEvent ev) => OpenEventDialog(ev);

    /// <summary>確認ダイアログを表示してイベントを削除する。</summary>
    private void DeleteEvent(ScheduleEvent ev)
    {
        if (MessageBox.Show($"「{ev.Title}」を削除しますか？", "削除確認",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _svc.Delete(ev.Id);
        ShowDayEvents(_selectedDate);
    }

    /// <summary>スケジュールイベントの追加・編集ダイアログを開く。</summary>
    private void OpenEventDialog(ScheduleEvent? existing)
    {
        var owner = Window.GetWindow(this);
        var dlg   = new Views.Dialogs.ScheduleEventDialog(existing, _selectedDate) { Owner = owner };
        if (dlg.ShowDialog() != true) return;

        if (existing == null)
        {
            _svc.Add(dlg.EventTitle, dlg.EventDescription, dlg.EventLocation,
                dlg.EventStart, dlg.EventEnd, dlg.EventIsAllDay, dlg.EventColor);
        }
        else
        {
            existing.Title       = dlg.EventTitle;
            existing.Description = dlg.EventDescription;
            existing.Location    = dlg.EventLocation;
            existing.StartTime   = dlg.EventStart;
            existing.EndTime     = dlg.EventEnd;
            existing.IsAllDay    = dlg.EventIsAllDay;
            existing.Color       = dlg.EventColor;
            _svc.Update(existing);
        }

        RenderCalendar();
        ShowDayEvents(_selectedDate);
    }

    // ── キーボード操作 ────────────────────────────────────
    /// <summary>カレンダー画面のキーボードショートカットを処理する。</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible) return;
        // Ctrl/Alt 併用は共通ショートカットに譲り、ここでは単独キーのみ扱う
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;

        switch (e.Key)
        {
            case Key.Left:     SelectDate(_selectedDate.AddDays(-1)); e.Handled = true; break;
            case Key.Right:    SelectDate(_selectedDate.AddDays(1));  e.Handled = true; break;
            case Key.Up:       SelectDate(_selectedDate.AddDays(-7)); e.Handled = true; break;
            case Key.Down:     SelectDate(_selectedDate.AddDays(7));  e.Handled = true; break;
            case Key.PageUp:   PrevMonth_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.PageDown: NextMonth_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Home:
            case Key.T:        Today_Click(this, new RoutedEventArgs()); e.Handled = true; break;
        }
    }

    // ── ヘッダーボタン ────────────────────────────────────
    /// <summary>完了タスクの表示・非表示を切り替える。</summary>
    private void ToggleCompleted_Click(object sender, RoutedEventArgs e)
    {
        _hideCompleted = !_hideCompleted;
        ((Button)sender).Content = _hideCompleted ? "完了を表示" : "完了を非表示";
        Refresh();
    }

    /// <summary>前月へ移動してカレンダーを更新する。</summary>
    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(-1);
        _year = d.Year; _month = d.Month;
        Refresh();
    }

    /// <summary>翌月へ移動してカレンダーを更新する。</summary>
    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(1);
        _year = d.Year; _month = d.Month;
        Refresh();
    }

    /// <summary>今日の日付へ戻ってカレンダーを更新する。</summary>
    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _selectedDate = DateTime.Today;
        _year  = DateTime.Today.Year;
        _month = DateTime.Today.Month;
        Refresh();
    }
}

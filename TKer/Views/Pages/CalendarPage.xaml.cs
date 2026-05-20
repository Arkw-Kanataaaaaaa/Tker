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


public partial class CalendarPage : Page, IRefreshable
{
    private readonly MainViewModel  _vm;
    private readonly ScheduleService _svc;

    private int      _year;
    private int      _month;
    private DateTime _selectedDate = DateTime.Today;
    private bool     _hideCompleted = false;

    public CalendarPage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = vm.ScheduleService;
        InitializeComponent();
        _year  = DateTime.Today.Year;
        _month = DateTime.Today.Month;
        _svc.DataChanged += OnDataChanged;
        Loaded   += (_, _) => Refresh();
        Unloaded += (_, _) => _svc.DataChanged -= OnDataChanged;
    }

    private void OnDataChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RenderCalendar);

    public void NavigateTo(DateTime date)
    {
        _selectedDate = date.Date;
        _year  = date.Year;
        _month = date.Month;
    }

    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Cal_Header"));
        MonthLabel.Text = $"{_year}年 {_month}月";
        RenderCalendar();
        ShowDayEvents(_selectedDate);
    }

    // ── カレンダー描画 ────────────────────────────────────
    private void RenderCalendar()
    {
        MonthLabel.Text = $"{_year}年 {_month}月";
        CalGrid.Children.Clear();

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
        // 翌月埋め
        int total = startDow + daysInMon;
        int rows  = (int)Math.Ceiling(total / 7.0) * 7;
        for (int i = total + 1; i <= rows; i++)
        {
            int d = i - total;
            CalGrid.Children.Add(BuildDayCell(
                new DateTime(nextYear, nextMonth, d), false, scheduleEvents, allTasks, cellMap));
        }
    }

    private Border BuildDayCell(DateTime date, bool isCurrent,
        IReadOnlyList<ScheduleEvent> scheduleEvents,
        IReadOnlyList<TaskItem> allTasks,
        Dictionary<DateTime, CalendarCell> cellMap)
    {
        bool isToday    = date.Date == DateTime.Today;
        bool isSelected = date.Date == _selectedDate.Date;
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

        // ボーダー色
        Color borderColor;
        if (isSelected)
            borderColor = Color.FromRgb(0x00, 0xBF, 0xD8); // AccentCyan
        else if (isToday)
            borderColor = Color.FromRgb(35, 131, 226);
        else if (isHoliday)
            borderColor = Color.FromRgb(100, 55, 55);
        else
            borderColor = Color.FromRgb(55, 55, 55);

        var border = new Border
        {
            Margin          = new Thickness(2),
            Background      = new SolidColorBrush(bgColor),
            BorderBrush     = new SolidColorBrush(borderColor),
            BorderThickness = isSelected || isToday ? new Thickness(2) : new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            MinHeight       = 80,
            Padding         = new Thickness(5),
            Opacity         = isCurrent ? 1.0 : 0.35,
            Cursor          = Cursors.Hand
        };

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
        border.MouseLeftButtonUp += (_, _) =>
        {
            _selectedDate = date;
            RenderCalendar();           // 選択枠の再描画
            ShowDayEvents(date);
        };
        return border;
    }

    // ── 日別詳細パネル ────────────────────────────────────
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

        // プロジェクトタスク
        var tasks = (_vm.ProjectService.CurrentProject?.Tasks ?? new List<TaskItem>())
            .Where(t => t.PlannedStartDate?.Date <= date.Date && t.PlannedEndDate?.Date >= date.Date)
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
                EventListPanel.Children.Add(BuildTaskChip(t));
        }
    }

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

    private Border BuildTaskChip(TaskItem t)
    {
        return new Border
        {
            Margin          = new Thickness(4, 2, 4, 0),
            Padding         = new Thickness(10, 6, 10, 6),
            CornerRadius    = new CornerRadius(6),
            Background      = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(120, 0x66, 0x66, 0x99)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text       = t.Name,
                        FontSize   = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(207, 207, 207))
                    },
                    new TextBlock
                    {
                        Text       = $"{t.PlannedStartDate:MM/dd} 〜 {t.PlannedEndDate:MM/dd}  [{t.Status}]",
                        FontSize   = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                        Margin     = new Thickness(0, 2, 0, 0)
                    }
                }
            }
        };
    }

    // ── イベント追加・編集・削除 ──────────────────────────
    private void BtnAddEvent_Click(object sender, RoutedEventArgs e) => OpenEventDialog(null);

    private void EditEvent(ScheduleEvent ev) => OpenEventDialog(ev);

    private void DeleteEvent(ScheduleEvent ev)
    {
        if (MessageBox.Show($"「{ev.Title}」を削除しますか？", "削除確認",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _svc.Delete(ev.Id);
        ShowDayEvents(_selectedDate);
    }

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

    // ── ヘッダーボタン ────────────────────────────────────
    private void ToggleCompleted_Click(object sender, RoutedEventArgs e)
    {
        _hideCompleted = !_hideCompleted;
        ((Button)sender).Content = _hideCompleted ? "完了を表示" : "完了を非表示";
        Refresh();
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(-1);
        _year = d.Year; _month = d.Month;
        Refresh();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(1);
        _year = d.Year; _month = d.Month;
        Refresh();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _selectedDate = DateTime.Today;
        _year  = DateTime.Today.Year;
        _month = DateTime.Today.Month;
        Refresh();
    }
}

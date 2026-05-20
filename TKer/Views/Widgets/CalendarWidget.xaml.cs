using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;

namespace TKer.Views.Widgets;

public partial class CalendarWidget : Window
{
    // ── Win32 ──────────────────────────────────────────────
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    static readonly IntPtr HWND_TOPMOST    = new(-1);
    static readonly IntPtr HWND_NOTOPMOST  = new(-2);
    static readonly IntPtr HWND_BOTTOM     = new(1);
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_NOMOVE     = 0x0002;
    const uint SWP_NOSIZE     = 0x0001;

    // ── フィールド ─────────────────────────────────────────
    private readonly ScheduleService     _svc;
    private readonly ProjectService      _proj;
    private readonly AppSettingsService  _appSettings;
    private CalendarWidgetSettings       _ws;

    private int      _year;
    private int      _month;
    private bool     _suppressSave = false;
    private bool     _forceClose   = false;

    private DispatcherTimer? _autoRefreshTimer;
    private DispatcherTimer? _desktopModeTimer;

    // ── コンストラクタ ────────────────────────────────────
    public CalendarWidget(ScheduleService svc, ProjectService proj, AppSettingsService appSettings)
    {
        _svc         = svc;
        _proj        = proj;
        _appSettings = appSettings;
        _ws          = appSettings.WidgetSettings;

        InitializeComponent();

        _year  = DateTime.Today.Year;
        _month = DateTime.Today.Month;

        // 位置・サイズ復元
        _suppressSave = true;
        Left   = _ws.Left;
        Top    = _ws.Top;
        Width  = _ws.Width;
        Height = _ws.Height;
        _suppressSave = false;

        // データ変更時の自動更新
        _svc.DataChanged += (_, _) => Dispatcher.Invoke(Render);

        // 5分ごとに自動更新
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _autoRefreshTimer.Tick += (_, _) => Render();
        _autoRefreshTimer.Start();

        Loaded += (_, _) =>
        {
            ApplySettings();
            ApplyWindowMode();
        };
    }

    // ── 設定適用 ──────────────────────────────────────────
    private void ApplySettings()
    {
        _ws = _appSettings.WidgetSettings;

        // 背景・ボーダー
        try
        {
            var bg = (Color)ColorConverter.ConvertFromString(_ws.BackgroundColor);
            RootBorder.Background = new SolidColorBrush(bg);
        }
        catch { RootBorder.Background = new SolidColorBrush(Color.FromArgb(230, 26, 31, 46)); }

        try
        {
            var bc = (Color)ColorConverter.ConvertFromString(_ws.BorderColor);
            RootBorder.BorderBrush     = new SolidColorBrush(bc);
            RootBorder.BorderThickness = new Thickness(_ws.BorderThickness);
        }
        catch { RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 58, 69, 96)); }

        RootBorder.CornerRadius = new CornerRadius(_ws.CornerRadius);
        Opacity     = _ws.Opacity;
        Topmost     = _ws.AlwaysOnTop;

        // テキスト色を各ボタンに適用
        var fg = ParseBrush(_ws.TextColor);
        TxtPrevIcon.Foreground  = fg;
        TxtNextIcon.Foreground  = fg;
        TxtMonthYear.Foreground = fg;
        TxtMonthYear.FontSize   = _ws.FontSize;
        TxtCloseIcon.Foreground = fg;
        TxtResizeGrip.Foreground = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));

        // ピンアイコン更新
        UpdatePinIcon();

        Render();
    }

    private void ApplyWindowMode()
    {
        _desktopModeTimer?.Stop();
        _desktopModeTimer = null;

        if (!IsLoaded) return;
        var hwnd = new WindowInteropHelper(this).Handle;

        if (_ws.DesktopMode)
        {
            Topmost = false;
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            // デスクトップモード: 1秒ごとに最背面を維持
            _desktopModeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _desktopModeTimer.Tick += (_, _) =>
            {
                if (!_ws.DesktopMode) { _desktopModeTimer?.Stop(); return; }
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            };
            _desktopModeTimer.Start();
        }
        else if (_ws.AlwaysOnTop)
        {
            Topmost = true;
        }
        else
        {
            Topmost = false;
        }
    }

    private void UpdatePinIcon()
    {
        if (_ws.DesktopMode)
        {
            TxtPinIcon.Text              = "🖥";
            BtnPinIcon.ToolTip           = "デスクトップモード: ON（クリックで解除）";
            TxtPinIcon.Foreground        = ParseBrush(_ws.AccentColor);
        }
        else if (_ws.AlwaysOnTop)
        {
            TxtPinIcon.Text              = "📌";
            BtnPinIcon.ToolTip           = "最前面固定: ON（クリックで解除）";
            TxtPinIcon.Foreground        = ParseBrush(_ws.AccentColor);
        }
        else
        {
            TxtPinIcon.Text              = "📌";
            BtnPinIcon.ToolTip           = "クリックで最前面固定";
            TxtPinIcon.Foreground        = new SolidColorBrush(Color.FromArgb(120, 200, 200, 200));
        }
    }

    // ── カレンダー描画 ────────────────────────────────────
    private void Render()
    {
        TxtMonthYear.Text = $"{_year}年 {_month}月";

        var accent    = ParseColor(_ws.AccentColor);
        var textBrush = ParseBrush(_ws.TextColor);
        var dimBrush  = ParseBrush(_ws.DimTextColor);

        // ── 曜日ヘッダー ──
        DayHeaderGrid.Children.Clear();
        string[] days = { "日", "月", "火", "水", "木", "金", "土" };
        Color[]  dayColors =
        {
            Color.FromRgb(0xEF, 0x53, 0x50),  // 日: 赤
            ParseColor(_ws.DimTextColor), ParseColor(_ws.DimTextColor),
            ParseColor(_ws.DimTextColor), ParseColor(_ws.DimTextColor),
            ParseColor(_ws.DimTextColor),
            Color.FromRgb(0x42, 0xA5, 0xF5)   // 土: 青
        };
        for (int i = 0; i < 7; i++)
        {
            DayHeaderGrid.Children.Add(new TextBlock
            {
                Text              = days[i],
                FontSize          = Math.Max(9, _ws.FontSize - 2),
                Foreground        = new SolidColorBrush(dayColors[i]),
                TextAlignment     = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 2, 0, 2)
            });
        }

        // ── カレンダーセル ──
        CalGrid.Children.Clear();

        var first      = new DateTime(_year, _month, 1);
        int startDow   = (int)first.DayOfWeek;
        int daysInMon  = DateTime.DaysInMonth(_year, _month);
        int daysInPrev = DateTime.DaysInMonth(
            _month == 1 ? _year - 1 : _year,
            _month == 1 ? 12 : _month - 1);
        int prevYear  = _month == 1 ? _year - 1 : _year;
        int prevMonth = _month == 1 ? 12 : _month - 1;
        int nextYear  = _month == 12 ? _year + 1 : _year;
        int nextMonth = _month == 12 ? 1 : _month + 1;

        // イベント・タスク取得
        var events = _ws.ShowEvents ? _svc.GetByMonth(_year, _month) : new List<ScheduleEvent>();
        var tasks  = _ws.ShowTasks
            ? (_proj.CurrentProject?.Tasks ?? new System.Collections.Generic.List<TaskItem>())
            : new System.Collections.Generic.List<TaskItem>();

        // 前月
        for (int i = 0; i < startDow; i++)
        {
            int d = daysInPrev - startDow + 1 + i;
            CalGrid.Children.Add(BuildCell(new DateTime(prevYear, prevMonth, d), false, events, tasks));
        }
        // 今月
        for (int d = 1; d <= daysInMon; d++)
            CalGrid.Children.Add(BuildCell(new DateTime(_year, _month, d), true, events, tasks));
        // 翌月
        int total = startDow + daysInMon;
        int rows  = (int)Math.Ceiling(total / 7.0) * 7;
        for (int i = total + 1; i <= rows; i++)
            CalGrid.Children.Add(BuildCell(new DateTime(nextYear, nextMonth, i - total), false, events, tasks));

        // 行数を設定
        CalGrid.Rows = rows / 7;
    }

    private Border BuildCell(DateTime date, bool isCurrent,
        IEnumerable<ScheduleEvent> events, IEnumerable<TaskItem> tasks)
    {
        bool isToday   = date.Date == DateTime.Today;
        bool isHoliday = JapaneseHolidays.IsHoliday(date);
        bool isSun     = date.DayOfWeek == DayOfWeek.Sunday;
        bool isSat     = date.DayOfWeek == DayOfWeek.Saturday;

        // 日付テキスト色
        Color dayFg;
        if (!isCurrent)
            dayFg = ParseColor(_ws.DimTextColor, 0.4);
        else if (isToday)
            dayFg = ParseColor(_ws.AccentColor);
        else if (isSun || isHoliday)
            dayFg = Color.FromRgb(0xEF, 0x53, 0x50);
        else if (isSat)
            dayFg = Color.FromRgb(0x42, 0xA5, 0xF5);
        else
            dayFg = ParseColor(_ws.TextColor);

        // 背景
        Brush cellBg = isToday
            ? new SolidColorBrush(Color.FromArgb(50, ParseColor(_ws.AccentColor).R,
                ParseColor(_ws.AccentColor).G, ParseColor(_ws.AccentColor).B))
            : Brushes.Transparent;

        var sp = new StackPanel { Margin = new Thickness(1) };

        // 日付
        sp.Children.Add(new TextBlock
        {
            Text              = date.Day.ToString(),
            FontSize          = Math.Max(9, _ws.FontSize - 1),
            FontWeight        = isToday ? FontWeights.Bold : FontWeights.Normal,
            Foreground        = new SolidColorBrush(dayFg),
            TextAlignment     = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        // イベントドット（最大3個）
        var dayEvents = events
            .Where(ev => ev.StartTime.Date <= date.Date && ev.EndTime.Date >= date.Date)
            .Take(3).ToList();
        var dayTasks = tasks
            .Where(t => t.PlannedStartDate?.Date <= date.Date && t.PlannedEndDate?.Date >= date.Date)
            .Take(2).ToList();

        if (dayEvents.Any() || dayTasks.Any())
        {
            var dotSp = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 1, 0, 0)
            };
            foreach (var ev in dayEvents)
            {
                Color c;
                try { c = (Color)ColorConverter.ConvertFromString(ev.Color); }
                catch { c = ParseColor(_ws.AccentColor); }
                dotSp.Children.Add(MakeDot(c));
            }
            foreach (var _ in dayTasks)
                dotSp.Children.Add(MakeDot(Color.FromRgb(0x23, 0x83, 0xE2)));
            sp.Children.Add(dotSp);
        }

        var border = new Border
        {
            Background = cellBg,
            Child      = sp,
            Margin     = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Cursor     = Cursors.Hand
        };

        // ツールチップ
        if (dayEvents.Any() || dayTasks.Any())
        {
            var tip = new ToolTip { Background = new SolidColorBrush(Color.FromRgb(26, 31, 46)) };
            var tipSp = new StackPanel { Margin = new Thickness(6) };
            tipSp.Children.Add(new TextBlock
            {
                Text       = date.ToString("M月d日 (ddd)"),
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                FontSize   = 12,
                Margin     = new Thickness(0, 0, 0, 4)
            });
            foreach (var ev in dayEvents)
                tipSp.Children.Add(new TextBlock
                {
                    Text = $"• {ev.Title}", Foreground = Brushes.LightGray, FontSize = 11
                });
            foreach (var t in dayTasks)
                tipSp.Children.Add(new TextBlock
                {
                    Text = $"📋 {t.Name}", Foreground = Brushes.LightGray, FontSize = 11
                });
            tip.Content = tipSp;
            ToolTipService.SetToolTip(border, tip);
        }

        border.MouseEnter += (_, _) =>
        {
            if (!isToday)
                border.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        };
        border.MouseLeave += (_, _) => border.Background = cellBg;

        return border;
    }

    private static Ellipse MakeDot(Color c) => new()
    {
        Width  = 5, Height = 5,
        Fill   = new SolidColorBrush(c),
        Margin = new Thickness(1, 0, 1, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    // ── ヘッダーボタン ────────────────────────────────────
    private void BtnPrev_Click(object s, RoutedEventArgs e)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(-1);
        _year = d.Year; _month = d.Month;
        Render();
    }

    private void BtnNext_Click(object s, RoutedEventArgs e)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(1);
        _year = d.Year; _month = d.Month;
        Render();
    }

    private void BtnClose_Click(object s, RoutedEventArgs e)
    {
        _ws.IsVisible = false;
        _appSettings.SaveWidgetSettings(_ws);
        Hide();
    }

    private void BtnPin_Click(object s, RoutedEventArgs e)
    {
        if (_ws.DesktopMode)
        {
            // デスクトップモード → 通常へ
            _ws.DesktopMode  = false;
            _ws.AlwaysOnTop  = false;
        }
        else if (_ws.AlwaysOnTop)
        {
            // 最前面 → デスクトップモードへ
            _ws.AlwaysOnTop = false;
            _ws.DesktopMode = true;
        }
        else
        {
            // 通常 → 最前面へ
            _ws.AlwaysOnTop = true;
        }
        _appSettings.SaveWidgetSettings(_ws);
        ApplyWindowMode();
        UpdatePinIcon();
    }

    private void BtnSettings_Click(object s, RoutedEventArgs e)
        => ShowSettingsDialog();

    // ── ドラッグ移動 ─────────────────────────────────────
    private void Header_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // ── リサイズグリップ ──────────────────────────────────
    private void ResizeGrip_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            ReleaseMouseCapture();
            // WM_NCLBUTTONDOWN + HTBOTTOMRIGHT → ウィンドウのネイティブリサイズ
            SendMessage(hwnd, 0xA1, (IntPtr)17, IntPtr.Zero);
        }
    }

    // ── 位置・サイズ保存 ──────────────────────────────────
    private void Widget_LocationChanged(object s, EventArgs e)
    {
        if (_suppressSave || !IsLoaded) return;
        _ws.Left = Left;
        _ws.Top  = Top;
        _appSettings.SaveWidgetSettings(_ws);
    }

    private void Widget_SizeChanged(object s, SizeChangedEventArgs e)
    {
        if (_suppressSave || !IsLoaded) return;
        _ws.Width  = Width;
        _ws.Height = Height;
        _appSettings.SaveWidgetSettings(_ws);
    }

    private void Widget_Closing(object s, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose) return; // アプリ終了時はそのまま閉じる

        // × ボタンで閉じた場合はHideに変えて非表示
        e.Cancel = true;
        _ws.IsVisible = false;
        _appSettings.SaveWidgetSettings(_ws);
        Hide();
    }

    /// <summary>アプリ終了時など、強制的にウィンドウを閉じる</summary>
    public void ForceClose()
    {
        _forceClose = true;
        _desktopModeTimer?.Stop();
        _autoRefreshTimer?.Stop();
        Close();
    }

    // ── 設定ダイアログ ────────────────────────────────────
    private void ShowSettingsDialog()
    {
        var dlg = new WidgetSettingsDialog(_ws) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        _ws = dlg.Result;
        _appSettings.SaveWidgetSettings(_ws);
        ApplySettings();
        ApplyWindowMode();
    }

    // ── ヘルパー ──────────────────────────────────────────
    private static SolidColorBrush ParseBrush(string hex, double opacity = 1.0)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(Color.FromArgb(
                (byte)(c.A * opacity), c.R, c.G, c.B));
        }
        catch { return new SolidColorBrush(Colors.Gray); }
    }

    private static Color ParseColor(string hex, double opacity = 1.0)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return Color.FromArgb((byte)(c.A * opacity), c.R, c.G, c.B);
        }
        catch { return Colors.Gray; }
    }

    // ── 公開メソッド（MainWindow から呼び出し） ────────────
    public void ShowWidget()
    {
        _ws = _appSettings.WidgetSettings;
        _suppressSave = true;
        Left   = _ws.Left;
        Top    = _ws.Top;
        Width  = _ws.Width;
        Height = _ws.Height;
        _suppressSave = false;

        ApplySettings();
        ApplyWindowMode();
        Show();

        _ws.IsVisible = true;
        _appSettings.SaveWidgetSettings(_ws);
    }
}

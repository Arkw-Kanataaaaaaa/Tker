using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;

namespace TKer.Views.Pages;

/// <summary>ポモドーロタイマーの操作と統計表示を行うページ。</summary>
public partial class PomodoroPage : Page, IRefreshable
{
    // ── Win32 メディア停止 ─────────────────────────────────
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const byte VK_MEDIA_STOP = 0xB2;

    private enum PomodoroMode { Work, ShortBreak, LongBreak }

    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    // 状態
    private PomodoroMode _mode = PomodoroMode.Work;
    private bool _isRunning   = false;
    private int  _remaining   = 0;   // 残り秒
    private int  _totalSecs   = 0;   // そのフェーズの総秒
    private int  _sessionsDone = 0;  // 完了した作業セッション数（サイクル内）
    private int  _todaySessions = 0;
    private int  _todayMinutes  = 0;
    private int  _todayBreaks   = 0;

    // 設定（UIから読む）
    private int WorkMin  => ParseMin(TxtWorkMin.Text,  25);
    private int ShortMin => ParseMin(TxtShortMin.Text, 5);
    private int LongMin  => ParseMin(TxtLongMin.Text,  15);
    private int MaxSessions => ParseMin(TxtSessions.Text, 4);

    /// <summary>ポモドーロページを初期化してタイマーイベントを設定する。</summary>
    public PomodoroPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => Refresh();
    }

    /// <summary>テーマとポモドーロ設定を読み込んで画面を更新する。</summary>
    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Pomo_Header"));
        var s = _vm.AppSettingsService.Settings.Pomodoro;
        TxtWorkMin.Text   = s.WorkMinutes.ToString();
        TxtShortMin.Text  = s.ShortBreakMinutes.ToString();
        TxtLongMin.Text   = s.LongBreakMinutes.ToString();
        TxtSessions.Text  = s.SessionsBeforeLongBreak.ToString();
        ChkStopMedia.IsChecked = s.StopMediaOnBreak;
        ChkNotify.IsChecked    = s.NotifyOnComplete;

        if (!_isRunning && _remaining == 0)
            ResetTimer();
    }

    // ── タイマー制御 ─────────────────────────────────────
    /// <summary>タイマーの開始・一時停止を切り替える。</summary>
    private void BtnStartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            _timer.Stop();
            _isRunning = false;
            BtnStartStop.Content = "▶ 再開";
        }
        else
        {
            if (_remaining == 0) ResetTimer();
            _timer.Start();
            _isRunning = true;
            BtnStartStop.Content = "⏸ 一時停止";
        }
    }

    /// <summary>タイマーをリセットして作業フェーズの最初に戻す。</summary>
    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _isRunning = false;
        _mode = PomodoroMode.Work;
        _sessionsDone = 0;
        ResetTimer();
    }

    /// <summary>現在のフェーズをスキップして次のフェーズに進む。</summary>
    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _isRunning = false;
        AdvanceMode(completed: false);
    }

    /// <summary>毎秒呼ばれて残り時間を減らしUIを更新する。</summary>
    private void Timer_Tick(object? sender, EventArgs e)
    {
        _remaining--;
        UpdateDisplay();
        if (_remaining <= 0)
        {
            _timer.Stop();
            _isRunning = false;
            OnPhaseComplete();
        }
    }

    /// <summary>フェーズ完了時に統計を更新し通知を出す。</summary>
    private void OnPhaseComplete()
    {
        if (_mode == PomodoroMode.Work)
        {
            _todaySessions++;
            _sessionsDone++;
            _todayMinutes += WorkMin;
            TxtTodaySessions.Text = _todaySessions.ToString();
            TxtTodayMinutes.Text  = _todayMinutes.ToString();
        }
        else
        {
            _todayBreaks++;
            TxtTodayBreaks.Text = _todayBreaks.ToString();
        }

        if (ChkNotify.IsChecked == true)
            System.Media.SystemSounds.Asterisk.Play();

        AdvanceMode(completed: true);
    }

    /// <summary>次のポモドーロモード（作業・休憩）に切り替える。</summary>
    private void AdvanceMode(bool completed)
    {
        if (_mode == PomodoroMode.Work)
        {
            // 長い休憩 or 短い休憩
            if (_sessionsDone > 0 && _sessionsDone % MaxSessions == 0)
                _mode = PomodoroMode.LongBreak;
            else
                _mode = PomodoroMode.ShortBreak;

            if (completed && ChkStopMedia.IsChecked == true)
                keybd_event(VK_MEDIA_STOP, 0, 0, UIntPtr.Zero);
        }
        else
        {
            _mode = PomodoroMode.Work;
        }
        ResetTimer();
    }

    /// <summary>現在のモードに合わせて残り時間を設定してUIをリセットする。</summary>
    private void ResetTimer()
    {
        _remaining = _mode switch
        {
            PomodoroMode.Work       => WorkMin  * 60,
            PomodoroMode.ShortBreak => ShortMin * 60,
            PomodoroMode.LongBreak  => LongMin  * 60,
            _ => WorkMin * 60
        };
        _totalSecs = _remaining;
        BtnStartStop.Content = "▶ 開始";
        UpdateDisplay();
        UpdateModeUI();
        UpdateSessionDots();
    }

    // ── UI 更新 ───────────────────────────────────────────
    /// <summary>残り時間テキストとプログレス円弧を更新する。</summary>
    private void UpdateDisplay()
    {
        int m = _remaining / 60, s = _remaining % 60;
        TxtTime.Text    = $"{m:D2}:{s:D2}";
        TxtSession.Text = $"セッション {_sessionsDone % MaxSessions + 1} / {MaxSessions}";
        DrawProgressArc();
    }

    /// <summary>現在のモードに応じたラベルと色をUIに反映する。</summary>
    private void UpdateModeUI()
    {
        (string label, Color color) = _mode switch
        {
            PomodoroMode.Work       => ("作業中",   Color.FromRgb(0x23, 0x83, 0xE2)),
            PomodoroMode.ShortBreak => ("短い休憩", Color.FromRgb(0x26, 0xA6, 0x9A)),
            PomodoroMode.LongBreak  => ("長い休憩", Color.FromRgb(0x38, 0x8E, 0x3C)),
            _ => ("作業中", Color.FromRgb(0x23, 0x83, 0xE2))
        };
        TxtMode.Text = label;
        ModeBadgeBrush.Color = color;
    }

    /// <summary>完了セッション数をドットインジケーターとして描画する。</summary>
    private void UpdateSessionDots()
    {
        SessionDots.Children.Clear();
        int max = MaxSessions;
        int done = _sessionsDone % max;
        for (int i = 0; i < max; i++)
        {
            var el = new Ellipse
            {
                Width = 14, Height = 14,
                Margin = new Thickness(4, 0, 4, 0),
                Fill = i < done
                    ? new SolidColorBrush(Color.FromRgb(0x23, 0x83, 0xE2))
                    : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x55))
            };
            SessionDots.Children.Add(el);
        }
    }

    /// <summary>経過率に応じたプログレス円弧をキャンバスに描画する。</summary>
    private void DrawProgressArc()
    {
        ProgressCanvas.Children.Clear();
        double ratio = _totalSecs > 0 ? 1.0 - (double)_remaining / _totalSecs : 1.0;
        if (ratio <= 0) return;

        double cx = 140, cy = 140, r = 128;
        double startAngle = -Math.PI / 2;
        double sweepAngle = 2 * Math.PI * ratio;

        var color = _mode switch
        {
            PomodoroMode.Work       => Color.FromRgb(0x23, 0x83, 0xE2),
            PomodoroMode.ShortBreak => Color.FromRgb(0x26, 0xA6, 0x9A),
            PomodoroMode.LongBreak  => Color.FromRgb(0x38, 0x8E, 0x3C),
            _ => Color.FromRgb(0x23, 0x83, 0xE2)
        };

        // 円弧を PathGeometry で描く
        double x1 = cx + r * Math.Cos(startAngle);
        double y1 = cy + r * Math.Sin(startAngle);
        double endAngle = startAngle + sweepAngle;
        double x2 = cx + r * Math.Cos(endAngle);
        double y2 = cy + r * Math.Sin(endAngle);
        bool largeArc = sweepAngle > Math.PI;

        var fig = new PathFigure { StartPoint = new Point(x1, y1) };
        fig.Segments.Add(new ArcSegment
        {
            Point            = new Point(x2, y2),
            Size             = new Size(r, r),
            SweepDirection   = SweepDirection.Clockwise,
            IsLargeArc       = largeArc,
        });

        var geo = new PathGeometry();
        geo.Figures.Add(fig);

        var path = new System.Windows.Shapes.Path
        {
            Stroke          = new SolidColorBrush(color),
            StrokeThickness = 10,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
            Data = geo
        };
        ProgressCanvas.Children.Add(path);
    }

    // ── 設定ボタン ────────────────────────────────────────
    private void BtnWorkPlus_Click(object s, RoutedEventArgs e)   => Adjust(TxtWorkMin,  1, 60);
    private void BtnWorkMinus_Click(object s, RoutedEventArgs e)  => Adjust(TxtWorkMin, -1, 1);
    private void BtnShortPlus_Click(object s, RoutedEventArgs e)  => Adjust(TxtShortMin, 1, 60);
    private void BtnShortMinus_Click(object s, RoutedEventArgs e) => Adjust(TxtShortMin,-1, 1);
    private void BtnLongPlus_Click(object s, RoutedEventArgs e)   => Adjust(TxtLongMin,  1, 60);
    private void BtnLongMinus_Click(object s, RoutedEventArgs e)  => Adjust(TxtLongMin, -1, 1);
    private void BtnSessionsPlus_Click(object s, RoutedEventArgs e)  => Adjust(TxtSessions,  1, 10);
    private void BtnSessionsMinus_Click(object s, RoutedEventArgs e) => Adjust(TxtSessions, -1, 1);

    /// <summary>テキストボックスの数値を増減してクランプする。</summary>
    private void Adjust(TextBox tb, int delta, int max)
    {
        int v = Math.Clamp(ParseMin(tb.Text, 1) + delta, 1, max);
        tb.Text = v.ToString();
    }

    private void Settings_Changed(object sender, RoutedEventArgs e) { /* live-update optional */ }

    /// <summary>入力値を検証してポモドーロ設定を保存しタイマーをリセットする。</summary>
    private void BtnApplySettings_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            MessageBox.Show("タイマー動作中は設定を変更できません。\nリセットしてから変更してください。",
                "設定変更", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var s = _vm.AppSettingsService.Settings.Pomodoro;
        s.WorkMinutes             = WorkMin;
        s.ShortBreakMinutes       = ShortMin;
        s.LongBreakMinutes        = LongMin;
        s.SessionsBeforeLongBreak = MaxSessions;
        s.StopMediaOnBreak        = ChkStopMedia.IsChecked == true;
        s.NotifyOnComplete        = ChkNotify.IsChecked == true;
        // AppSettingsService は内部的に _settings を参照しているので Save を呼ぶ
        _vm.AppSettingsService.SavePomodoro(s);

        _mode = PomodoroMode.Work;
        _sessionsDone = 0;
        ResetTimer();
        UpdateSessionDots();
    }

    /// <summary>テキストを分単位の整数に変換し失敗時はフォールバック値を返す。</summary>
    private static int ParseMin(string? txt, int fallback)
        => int.TryParse(txt, out var v) ? Math.Max(1, v) : fallback;
}

using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;
using TKer.Views;
using TKer.WidgetHost;

namespace TKer;

public partial class App : Application
{
    private WidgetHostApp? _widgetHostApp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── ウィジェット専用モード ──────────────────────────
        if (e.Args.Contains("--widget"))
        {
            StartWidgetMode();
            return;
        }

        // ── 通常モード ──────────────────────────────────────
        StartNormalMode();
    }

    // ── ウィジェット専用プロセス ──────────────────────────
    private void StartWidgetMode()
    {
        // 二重起動チェック
        if (WidgetHostApp.IsAlreadyRunning())
        {
            System.Windows.MessageBox.Show(
                "ウィジェットは既に起動しています。\nタスクトレイを確認してください。",
                "TKer ウィジェット",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // WPF をトレイアイコン常駐モードで動作させる
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 透明ダミーウィンドウ（WPF のメインウィンドウとして必要）
        var dummyWindow = new Window
        {
            Width  = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            IsHitTestVisible = false,
            Opacity = 0
        };
        dummyWindow.Show();
        MainWindow = dummyWindow;

        // テーマ適用（ウィジェットも AppTheme を使う）
        var settingsSvc = new AppSettingsService();
        ApplyTheme(settingsSvc.Theme);

        // 未処理例外ハンドラ
        DispatcherUnhandledException += (s, ex) =>
        {
            AppLogger.Instance.Error("App", "DispatcherUnhandledException", ex.Exception.Message, ex.Exception);
            ex.Handled = true;
        };

        // ウィジェットホスト起動
        _widgetHostApp = new WidgetHostApp();
        _widgetHostApp.Start();
    }

    // ── 通常モード ────────────────────────────────────────
    private void StartNormalMode()
    {
        // ハードウェアアクセラレーション強制
        RenderOptions.ProcessRenderMode = RenderMode.Default;
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(System.Windows.Media.Animation.Timeline),
            new FrameworkPropertyMetadata(60));

        // スムーズスクロールを全ScrollViewerに適用
        SmoothScroll.Register();

        // テーマを適用
        var svc = new AppSettingsService();
        ApplyTheme(svc.Theme);

        // 未処理例外ハンドラ
        DispatcherUnhandledException += (s, ex) =>
        {
            System.Windows.MessageBox.Show($"予期しないエラーが発生しました:\n{ex.Exception.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        // メインウィンドウを表示
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _widgetHostApp?.Dispose();
        base.OnExit(e);
    }

    public static void ApplyTheme(ThemeColors t)
    {
        var res = Current.Resources;
        if (!string.IsNullOrEmpty(t.TextPrimary)  && TryParse(t.TextPrimary,  out var c1)) res["TextPrimaryBrush"]   = new SolidColorBrush(c1);
        if (!string.IsNullOrEmpty(t.TextSecond)   && TryParse(t.TextSecond,   out var c2)) res["TextSecondaryBrush"] = new SolidColorBrush(c2);
        if (!string.IsNullOrEmpty(t.AccentCyan)   && TryParse(t.AccentCyan,   out var c3)) res["AccentCyanBrush"]    = new SolidColorBrush(c3);
        if (!string.IsNullOrEmpty(t.BgSecondary)  && TryParse(t.BgSecondary,  out var c4)) res["BgSecondaryBrush"]  = new SolidColorBrush(c4);
        if (!string.IsNullOrEmpty(t.Border)       && TryParse(t.Border,       out var c6)) res["BorderBrush"]       = new SolidColorBrush(c6);

        // カード背景色（不透明度を加味）
        Color cardColor;
        bool hasCardColor = !string.IsNullOrEmpty(t.BgCard) && TryParse(t.BgCard, out cardColor);
        if (!hasCardColor && res["BgCardBrush"] is SolidColorBrush existing)
            cardColor = existing.Color;
        if (hasCardColor || t.BgCardOpacity.HasValue)
        {
            byte alpha = t.BgCardOpacity.HasValue
                ? (byte)Math.Clamp((int)(t.BgCardOpacity.Value * 255), 0, 255)
                : (byte)255;
            cardColor.A = alpha;
            res["BgCardBrush"] = new SolidColorBrush(cardColor);
        }

        // ボタン背景色（設定済みの場合のみ上書き）
        if (!string.IsNullOrEmpty(t.ButtonBg) && TryParse(t.ButtonBg, out var btnBg))
        {
            res["ButtonBgBrush"]    = new SolidColorBrush(btnBg);
            res["ButtonHoverBrush"] = new SolidColorBrush(Darken(btnBg, 0.75));
        }
        if (!string.IsNullOrEmpty(t.ButtonBorder) && TryParse(t.ButtonBorder, out var btnBdr))
            res["ButtonBorderBrush"] = new SolidColorBrush(btnBdr);

        // ドロップダウン背景色
        if (!string.IsNullOrEmpty(t.DropdownBg) && TryParse(t.DropdownBg, out var ddBg))
            res["DropdownBgBrush"] = new SolidColorBrush(ddBg);
    }

    private static Color Darken(Color c, double factor)
        => Color.FromArgb(c.A,
            (byte)(c.R * factor),
            (byte)(c.G * factor),
            (byte)(c.B * factor));

    public static void ApplyFont(string? fontFamily, System.Windows.Window window)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
            window.FontFamily = new FontFamily(fontFamily);
    }

    private static bool TryParse(string hex, out Color color)
    {
        try { color = (Color)ColorConverter.ConvertFromString(hex); return true; }
        catch { color = default; return false; }
    }
}

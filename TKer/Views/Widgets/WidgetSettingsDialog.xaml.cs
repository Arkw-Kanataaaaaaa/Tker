using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Models;

namespace TKer.Views.Widgets;

/// <summary>カレンダーウィジェットの外観・動作設定を編集するダイアログウィンドウ。</summary>
public partial class WidgetSettingsDialog : Window
{
    // 編集中の設定コピー
    private CalendarWidgetSettings _ws;

    /// <summary>OKを押したときに確定した設定を返す</summary>
    public CalendarWidgetSettings Result { get; private set; } = new();

    /// <summary>現在の設定のディープコピーを作成してダイアログを初期化する。</summary>
    public WidgetSettingsDialog(CalendarWidgetSettings current)
    {
        InitializeComponent();

        // 現在設定のディープコピーで編集
        _ws = CloneSettings(current);

        LoadValues();
    }

    // ── 値の読み込み ──────────────────────────────────────
    /// <summary>編集中の設定値をUIコントロールに読み込む。</summary>
    private void LoadValues()
    {
        // 色
        TxtBgColor.Text      = _ws.BackgroundColor;
        TxtAccentColor.Text  = _ws.AccentColor;
        TxtTextColor.Text    = _ws.TextColor;
        TxtDimColor.Text     = _ws.DimTextColor;
        TxtBorderColor.Text  = _ws.BorderColor;

        // スライダー
        SldrOpacity.Value         = _ws.Opacity;
        SldrBorderThickness.Value = _ws.BorderThickness;
        SldrCornerRadius.Value    = _ws.CornerRadius;
        SldrFontSize.Value        = _ws.FontSize;

        // トグル
        TglShowEvents.IsChecked = _ws.ShowEvents;
        TglShowTasks.IsChecked  = _ws.ShowTasks;

        // ラベル更新
        UpdateSliderLabels();
        UpdateColorPreviews();
    }

    // ── イベントハンドラー ─────────────────────────────────
    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateSliderLabels();

    private void ColorText_Changed(object sender, TextChangedEventArgs e)
        => UpdateColorPreviews();

    /// <summary>各スライダーの現在値をラベルテキストとして更新する。</summary>
    private void UpdateSliderLabels()
    {
        LblOpacity.Text          = $"{SldrOpacity.Value:F2}";
        LblBorderThickness.Text  = $"{SldrBorderThickness.Value:F1}";
        LblCornerRadius.Text     = $"{(int)SldrCornerRadius.Value}";
        LblFontSize.Text         = $"{(int)SldrFontSize.Value}";
    }

    /// <summary>各カラーテキストボックスの値に基づいてプレビューボーダーの背景色を更新する。</summary>
    private void UpdateColorPreviews()
    {
        SetPreview(PreviewBg,     TxtBgColor.Text);
        SetPreview(PreviewAccent, TxtAccentColor.Text);
        SetPreview(PreviewText,   TxtTextColor.Text);
        SetPreview(PreviewDim,    TxtDimColor.Text);
        SetPreview(PreviewBorder, TxtBorderColor.Text);
    }

    /// <summary>16進数カラー文字列をパースしてプレビューBorderの背景色に設定する。</summary>
    private static void SetPreview(System.Windows.Controls.Border preview, string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            preview.Background = new SolidColorBrush(color);
        }
        catch
        {
            preview.Background = Brushes.Transparent;
        }
    }

    // ── サイズプリセット ──────────────────────────────────
    private void Preset_Small(object s, RoutedEventArgs e)  { _ws.Width = 220; _ws.Height = 240; }
    private void Preset_Medium(object s, RoutedEventArgs e) { _ws.Width = 280; _ws.Height = 300; }
    private void Preset_Large(object s, RoutedEventArgs e)  { _ws.Width = 340; _ws.Height = 370; }
    private void Preset_XLarge(object s, RoutedEventArgs e) { _ws.Width = 400; _ws.Height = 440; }

    // ── テーマプリセット ──────────────────────────────────
    private void Theme_Dark(object s, RoutedEventArgs e)
        => ApplyTheme("#E61A1F2E", "#3D7EFF", "#CFCFCF", "#787878", "#3A4560");

    private void Theme_Midnight(object s, RoutedEventArgs e)
        => ApplyTheme("#F00D1117", "#7C3AED", "#E2E8F0", "#64748B", "#2D2D3D");

    private void Theme_Ocean(object s, RoutedEventArgs e)
        => ApplyTheme("#F0082040", "#00BCD4", "#E0F7FA", "#607D8B", "#003B5C");

    private void Theme_Forest(object s, RoutedEventArgs e)
        => ApplyTheme("#F0071A14", "#4CAF50", "#C8E6C9", "#558B2F", "#1B3A22");

    private void Theme_Light(object s, RoutedEventArgs e)
        => ApplyTheme("#F0F5F7FF", "#1565C0", "#212121", "#757575", "#BBDEFB");

    /// <summary>指定されたテーマカラーをカラーテキストボックスに一括設定する。</summary>
    private void ApplyTheme(string bg, string accent, string text, string dim, string border)
    {
        TxtBgColor.Text     = bg;
        TxtAccentColor.Text = accent;
        TxtTextColor.Text   = text;
        TxtDimColor.Text    = dim;
        TxtBorderColor.Text = border;
    }

    // ── OK / キャンセル ───────────────────────────────────
    /// <summary>UI入力値を設定オブジェクトに収集してResultに設定し、ダイアログをtrueで閉じる。</summary>
    private void BtnOk_Click(object s, RoutedEventArgs e)
    {
        // 現在の入力を収集
        _ws.BackgroundColor = TxtBgColor.Text.Trim();
        _ws.AccentColor     = TxtAccentColor.Text.Trim();
        _ws.TextColor       = TxtTextColor.Text.Trim();
        _ws.DimTextColor    = TxtDimColor.Text.Trim();
        _ws.BorderColor     = TxtBorderColor.Text.Trim();

        _ws.Opacity         = SldrOpacity.Value;
        _ws.BorderThickness = SldrBorderThickness.Value;
        _ws.CornerRadius    = SldrCornerRadius.Value;
        _ws.FontSize        = (int)SldrFontSize.Value;

        _ws.ShowEvents = TglShowEvents.IsChecked == true;
        _ws.ShowTasks  = TglShowTasks.IsChecked  == true;

        Result = _ws;
        DialogResult = true;
    }

    private void BtnCancel_Click(object s, RoutedEventArgs e)
        => DialogResult = false;

    // ── ユーティリティ ────────────────────────────────────
    /// <summary>設定オブジェクトを全フィールドコピーしてディープコピーを生成する。</summary>
    private static CalendarWidgetSettings CloneSettings(CalendarWidgetSettings src) => new()
    {
        Left            = src.Left,
        Top             = src.Top,
        Width           = src.Width,
        Height          = src.Height,
        Opacity         = src.Opacity,
        BackgroundColor = src.BackgroundColor,
        AccentColor     = src.AccentColor,
        TextColor       = src.TextColor,
        DimTextColor    = src.DimTextColor,
        BorderColor     = src.BorderColor,
        BorderThickness = src.BorderThickness,
        CornerRadius    = src.CornerRadius,
        FontSize        = src.FontSize,
        AlwaysOnTop     = src.AlwaysOnTop,
        DesktopMode     = src.DesktopMode,
        ShowEvents      = src.ShowEvents,
        ShowTasks       = src.ShowTasks,
        IsVisible       = src.IsVisible,
    };
}

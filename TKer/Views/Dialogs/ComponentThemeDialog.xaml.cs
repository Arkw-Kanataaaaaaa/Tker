using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Models;

namespace TKer.Views.Dialogs;

/// <summary>コンポーネントの背景色・文字色・ボーダー色・透明度を設定するダイアログ。</summary>
public partial class ComponentThemeDialog : Window
{
    public SectionTheme Result { get; private set; } = new();

    /// <summary>コンポーネント名と現在のテーマ設定を受け取り初期値をセットする。</summary>
    public ComponentThemeDialog(string componentName, SectionTheme current)
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));
        ComponentLabel.Text   = componentName;
        TxtBgColor.Text       = current.BgColor     ?? "";
        TxtTextColor.Text     = current.TextColor    ?? "";
        TxtBorderColor.Text   = current.BorderColor  ?? "";
        SliderOpacity.Value   = Math.Clamp(current.Opacity * 100, 0, 100);
        UpdateBgPreview();
        UpdateTextPreview();
        UpdateBorderPreview();
    }

    private void TxtBgColor_TextChanged(object sender, TextChangedEventArgs e)     => UpdateBgPreview();
    private void TxtTextColor_TextChanged(object sender, TextChangedEventArgs e)   => UpdateTextPreview();
    private void TxtBorderColor_TextChanged(object sender, TextChangedEventArgs e) => UpdateBorderPreview();

    /// <summary>透明度スライダーの値が変化したときにラベルを更新する。</summary>
    private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LblOpacity != null) LblOpacity.Text = $"{(int)e.NewValue}%";
    }

    /// <summary>背景色テキストを解析してプレビューを更新する。</summary>
    private void UpdateBgPreview()
    {
        var c = TryParseColor(TxtBgColor.Text);
        BgPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
    }

    /// <summary>文字色テキストを解析してプレビューを更新する。</summary>
    private void UpdateTextPreview()
    {
        var c = TryParseColor(TxtTextColor.Text);
        TextPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
    }

    /// <summary>ボーダー色テキストを解析してプレビューを更新する。</summary>
    private void UpdateBorderPreview()
    {
        var c = TryParseColor(TxtBorderColor.Text);
        BorderPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
    }

    /// <summary>カラー文字列をパースしてColorを返す。無効な場合はnullを返す。</summary>
    private static Color? TryParseColor(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return (Color)ColorConverter.ConvertFromString(s); }
        catch { return null; }
    }

    /// <summary>入力値をResultに設定してダイアログを確定する。</summary>
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        Result = new SectionTheme
        {
            BgColor     = NullIfEmpty(TxtBgColor.Text),
            TextColor   = NullIfEmpty(TxtTextColor.Text),
            BorderColor = NullIfEmpty(TxtBorderColor.Text),
            Opacity     = SliderOpacity.Value / 100.0
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

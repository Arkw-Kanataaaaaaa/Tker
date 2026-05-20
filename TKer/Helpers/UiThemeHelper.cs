using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Models;

namespace TKer.Helpers;

/// <summary>
/// UI テーマ（背景色・文字色・ボーダー色）の適用を担うヘルパー。
/// </summary>
public static class UiThemeHelper
{
    /// <summary>SectionTheme 設定をカード Border 要素へ適用する。</summary>
    public static void ApplySectionTheme(Border card, SectionTheme theme)
    {
        if (!string.IsNullOrEmpty(theme.BgColor))
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(theme.BgColor);
                c.A = (byte)Math.Clamp((int)(theme.Opacity * 255), 0, 255);
                card.Background = new SolidColorBrush(c);
            }
            catch { }
        }
        else if (theme.Opacity < 1.0 && card.Background is SolidColorBrush sb)
        {
            var c = sb.Color;
            c.A = (byte)Math.Clamp((int)(theme.Opacity * 255), 0, 255);
            card.Background = new SolidColorBrush(c);
        }

        if (!string.IsNullOrEmpty(theme.TextColor))
        {
            try
            {
                var fg = (Color)ColorConverter.ConvertFromString(theme.TextColor);
                card.Resources["TextPrimaryBrush"] = new SolidColorBrush(fg);
            }
            catch { }
        }
        else
        {
            card.Resources.Remove("TextPrimaryBrush");
        }

        if (!string.IsNullOrEmpty(theme.BorderColor))
        {
            try
            {
                card.BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(theme.BorderColor));
            }
            catch { }
        }
    }

    /// <summary>
    /// 16進数カラーと不透明度から SolidColorBrush を生成して対象 Border の背景に適用する。
    /// パースに失敗した場合は Transparent を設定する。
    /// </summary>
    public static void ApplyColorBackground(Border target, string hexColor, double opacity)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            color.A = (byte)(opacity * 255);
            target.Background = new SolidColorBrush(color);
        }
        catch { target.Background = Brushes.Transparent; }
    }

}

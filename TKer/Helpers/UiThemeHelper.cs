using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Models;

namespace TKer.Helpers;

public static class UiThemeHelper
{
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

}

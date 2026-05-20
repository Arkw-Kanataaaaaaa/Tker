using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TKer.Helpers;

public static class UiBadgeHelper
{
    public static Border MakeBadge(string text, string hexColor, double margin = 0)
    {
        return new Border
        {
            Background = ParseBrush(hexColor, 0.25),
            BorderBrush = ParseBrush(hexColor, 0.7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, margin, 0),
            Child = new TextBlock
            {
                Text = text, FontSize = 11,
                Foreground = ParseBrush(hexColor),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    public static SolidColorBrush ParseBrush(string hex, double opacity = 1.0)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(Color.FromArgb(
                (byte)(c.A * opacity), c.R, c.G, c.B));
        }
        catch { return new SolidColorBrush(Colors.Gray); }
    }

    public static string StatusColor(string status) => status switch
    {
        "完了"       => "#4CAF50",
        "進行中"     => "#2196F3",
        "未着手"     => "#9E9E9E",
        "保留"       => "#FF9800",
        "レビュー中" => "#9C27B0",
        _            => "#607D8B"
    };

    public static string PriorityColor(string priority) => priority switch
    {
        "高" => "#EF5350",
        "中" => "#FFA726",
        "低" => "#78909C",
        _    => "#607D8B"
    };
}

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TKer.Models;

namespace TKer.Converters;

public class PriorityToBgConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "高" => new SolidColorBrush(Color.FromArgb(50,224,62,62)),
        "低" => new SolidColorBrush(Color.FromArgb(40,11,110,153)),
        _    => new SolidColorBrush(Color.FromArgb(50,217,115,13))
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
public class PriorityToFgConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "高" => new SolidColorBrush(Color.FromRgb(224,62,62)),
        "低" => new SolidColorBrush(Color.FromRgb(11,110,153)),
        _    => new SolidColorBrush(Color.FromRgb(217,115,13))
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
public class StatusToBgConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "完了"       => new SolidColorBrush(Color.FromArgb(40,82,158,114)),
        "対応中"     => new SolidColorBrush(Color.FromArgb(40,35,131,226)),
        "レビュー中" => new SolidColorBrush(Color.FromArgb(40,223,171,1)),
        _            => new SolidColorBrush(Color.FromArgb(40,72,72,72))
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
public class StatusToFgConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "完了"       => new SolidColorBrush(Color.FromRgb(82,158,114)),
        "対応中"     => new SolidColorBrush(Color.FromRgb(35,131,226)),
        "レビュー中" => new SolidColorBrush(Color.FromRgb(223,171,1)),
        _            => new SolidColorBrush(Color.FromRgb(120,119,116))
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
public class BoolToTypeConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is bool b && b ? "フォルダ" : "ファイル";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
public class StringToColorConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c) {
        try { var s = (v as string ?? "#2383E2"); if (!s.StartsWith('#')) s="#"+s;
              return (Color)ColorConverter.ConvertFromString(s); }
        catch { return Color.FromRgb(35,131,226); }
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
public class IntToVisibilityConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
public class AlertLevelToBorderConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is AlertLevel lv ? lv switch {
            AlertLevel.Overdue    => new SolidColorBrush(Color.FromArgb(180,224,62,62)),
            AlertLevel.DueSoon    => new SolidColorBrush(Color.FromArgb(180,223,171,1)),
            AlertLevel.NotStarted => new SolidColorBrush(Color.FromArgb(180,217,115,13)),
            _                     => new SolidColorBrush(Color.FromRgb(55,55,55))
        } : (object)new SolidColorBrush(Color.FromRgb(55,55,55));
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
public class AlertLevelToBgConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is AlertLevel lv ? lv switch {
            AlertLevel.Overdue    => new SolidColorBrush(Color.FromArgb(50,224,62,62)),
            AlertLevel.DueSoon    => new SolidColorBrush(Color.FromArgb(50,223,171,1)),
            AlertLevel.NotStarted => new SolidColorBrush(Color.FromArgb(50,217,115,13)),
            _                     => new SolidColorBrush(Color.FromArgb(40,72,72,72))
        } : (object)Brushes.Transparent;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
public class AlertLevelToFgConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is AlertLevel lv ? lv switch {
            AlertLevel.Overdue    => new SolidColorBrush(Color.FromRgb(224,62,62)),
            AlertLevel.DueSoon    => new SolidColorBrush(Color.FromRgb(223,171,1)),
            AlertLevel.NotStarted => new SolidColorBrush(Color.FromRgb(217,115,13)),
            _                     => new SolidColorBrush(Color.FromRgb(120,119,116))
        } : (object)Brushes.Gray;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
public class PercentToWidthConverter : IValueConverter {
    public object Convert(object v, Type t, object p, CultureInfo c) {
        if (v is double pct && p is string ps && double.TryParse(ps, out var maxW))
            return Math.Max(0, Math.Min(pct/100.0*maxW, maxW));
        return 0.0;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

// 遅延日数 → 色
public class DelayToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is int days)
            return days > 0
                ? new SolidColorBrush(Color.FromRgb(224, 62, 62))
                : days < 0
                    ? new SolidColorBrush(Color.FromRgb(82, 158, 114))
                    : new SolidColorBrush(Color.FromRgb(120, 119, 116));
        return new SolidColorBrush(Color.FromRgb(120, 119, 116));
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}


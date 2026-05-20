using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TKer.Models;

namespace TKer.Converters;

// ── 基底クラス ────────────────────────────────────────────────────────────────

/// <summary>
/// 単方向コンバーターの基底クラス。ConvertBack は Binding.DoNothing を返す。
/// </summary>
public abstract class OneWayConverter : IValueConverter
{
    public abstract object Convert(object value, Type targetType, object parameter, CultureInfo culture);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

// ── カラーパレット ─────────────────────────────────────────────────────────────

/// <summary>
/// アプリ共通のセマンティックカラー定義。マジックナンバーを一元管理する。
/// </summary>
internal static class AppPalette
{
    public static readonly Color Danger  = Color.FromRgb(224,  62,  62);
    public static readonly Color Warning = Color.FromRgb(223, 171,   1);
    public static readonly Color Caution = Color.FromRgb(217, 115,  13);
    public static readonly Color Success = Color.FromRgb( 82, 158, 114);
    public static readonly Color Info    = Color.FromRgb( 35, 131, 226);
    public static readonly Color Neutral = Color.FromRgb(120, 119, 116);
    public static readonly Color Dark    = Color.FromRgb( 55,  55,  55);
    public static readonly Color Muted   = Color.FromRgb( 72,  72,  72);
    public static readonly Color CoolBlue = Color.FromRgb(11, 110, 153);

    public static SolidColorBrush Brush(Color color, byte alpha = 255)
    {
        var c = color; c.A = alpha;
        return new SolidColorBrush(c);
    }
}

// ── コンバーター ──────────────────────────────────────────────────────────────

public class PriorityToBgConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "高" => AppPalette.Brush(AppPalette.Danger,  50),
        "低" => AppPalette.Brush(AppPalette.CoolBlue, 40),
        _    => AppPalette.Brush(AppPalette.Caution,  50)
    };
}

public class PriorityToFgConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "高" => AppPalette.Brush(AppPalette.Danger),
        "低" => AppPalette.Brush(AppPalette.CoolBlue),
        _    => AppPalette.Brush(AppPalette.Caution)
    };
}

public class StatusToBgConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "完了"       => AppPalette.Brush(AppPalette.Success, 40),
        "対応中"     => AppPalette.Brush(AppPalette.Info,    40),
        "レビュー中" => AppPalette.Brush(AppPalette.Warning, 40),
        _            => AppPalette.Brush(AppPalette.Muted,   40)
    };
}

public class StatusToFgConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "完了"       => AppPalette.Brush(AppPalette.Success),
        "対応中"     => AppPalette.Brush(AppPalette.Info),
        "レビュー中" => AppPalette.Brush(AppPalette.Warning),
        _            => AppPalette.Brush(AppPalette.Neutral)
    };
}

public class BoolToTypeConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c)
        => v is bool b && b ? "フォルダ" : "ファイル";
}

public class StringToColorConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c) {
        try {
            var s = (v as string ?? "#2383E2");
            if (!s.StartsWith('#')) s = "#" + s;
            return (Color)ColorConverter.ConvertFromString(s);
        }
        catch { return AppPalette.Info; }
    }
}

public class IntToVisibilityConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c)
        => v is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;
}

public class AlertLevelToBorderConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c) =>
        v is AlertLevel lv ? lv switch {
            AlertLevel.Overdue    => AppPalette.Brush(AppPalette.Danger,  180),
            AlertLevel.DueSoon    => AppPalette.Brush(AppPalette.Warning, 180),
            AlertLevel.NotStarted => AppPalette.Brush(AppPalette.Caution, 180),
            _                     => AppPalette.Brush(AppPalette.Dark)
        } : (object)AppPalette.Brush(AppPalette.Dark);
}

public class AlertLevelToBgConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c) =>
        v is AlertLevel lv ? lv switch {
            AlertLevel.Overdue    => AppPalette.Brush(AppPalette.Danger,  50),
            AlertLevel.DueSoon    => AppPalette.Brush(AppPalette.Warning, 50),
            AlertLevel.NotStarted => AppPalette.Brush(AppPalette.Caution, 50),
            _                     => AppPalette.Brush(AppPalette.Muted,   40)
        } : (object)Brushes.Transparent;
}

public class AlertLevelToFgConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c) =>
        v is AlertLevel lv ? lv switch {
            AlertLevel.Overdue    => AppPalette.Brush(AppPalette.Danger),
            AlertLevel.DueSoon    => AppPalette.Brush(AppPalette.Warning),
            AlertLevel.NotStarted => AppPalette.Brush(AppPalette.Caution),
            _                     => AppPalette.Brush(AppPalette.Neutral)
        } : (object)Brushes.Gray;
}

public class PercentToWidthConverter : OneWayConverter {
    public override object Convert(object v, Type t, object p, CultureInfo c) {
        if (v is double pct && p is string ps && double.TryParse(ps, out var maxW))
            return Math.Max(0, Math.Min(pct / 100.0 * maxW, maxW));
        return 0.0;
    }
}

public class DelayToColorConverter : OneWayConverter {
    public override object Convert(object value, Type t, object p, CultureInfo c) {
        if (value is int days)
            return days > 0 ? AppPalette.Brush(AppPalette.Danger)
                 : days < 0 ? AppPalette.Brush(AppPalette.Success)
                 :             AppPalette.Brush(AppPalette.Neutral);
        return AppPalette.Brush(AppPalette.Neutral);
    }
}

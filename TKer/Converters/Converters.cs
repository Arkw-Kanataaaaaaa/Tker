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
    /// <summary>値を変換する抽象メソッド。派生クラスで実装する。</summary>
    public abstract object Convert(object value, Type targetType, object parameter, CultureInfo culture);
    /// <summary>逆変換は未サポート。常に Binding.DoNothing を返す。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

// ── カラーパレット ─────────────────────────────────────────────────────────────

/// <summary>
/// アプリ共通のセマンティックカラー定義。マジックナンバーを一元管理する。
/// </summary>
internal static class AppPalette
{
    /// <summary>危険・エラーを示す赤系カラー。</summary>
    public static readonly Color DANGER   = Color.FromRgb(224,  62,  62);
    /// <summary>警告を示す黄系カラー。</summary>
    public static readonly Color WARNING  = Color.FromRgb(223, 171,   1);
    /// <summary>注意を示すオレンジ系カラー。</summary>
    public static readonly Color CAUTION  = Color.FromRgb(217, 115,  13);
    /// <summary>成功・完了を示す緑系カラー。</summary>
    public static readonly Color SUCCESS  = Color.FromRgb( 82, 158, 114);
    /// <summary>情報を示す青系カラー。</summary>
    public static readonly Color INFO     = Color.FromRgb( 35, 131, 226);
    /// <summary>中立・未設定を示すグレー系カラー。</summary>
    public static readonly Color NEUTRAL  = Color.FromRgb(120, 119, 116);
    /// <summary>暗いテキスト・ボーダー用のダークグレーカラー。</summary>
    public static readonly Color DARK     = Color.FromRgb( 55,  55,  55);
    /// <summary>控えめな表示用のミュートグレーカラー。</summary>
    public static readonly Color MUTED    = Color.FromRgb( 72,  72,  72);
    /// <summary>クールブルー系カラー。</summary>
    public static readonly Color COOL_BLUE = Color.FromRgb(11, 110, 153);

    /// <summary>指定カラーとアルファ値から SolidColorBrush を生成する。</summary>
    public static SolidColorBrush Brush(Color color, byte alpha = 255)
    {
        var c = color; c.A = alpha;
        return new SolidColorBrush(c);
    }
}

// ── コンバーター ──────────────────────────────────────────────────────────────

/// <summary>優先度文字列をバッジ背景色ブラシに変換するコンバーター。</summary>
public class PriorityToBgConverter : OneWayConverter {
    /// <summary>優先度に対応する背景色ブラシを返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "高" => AppPalette.Brush(AppPalette.DANGER,   50),
        "低" => AppPalette.Brush(AppPalette.COOL_BLUE, 40),
        _    => AppPalette.Brush(AppPalette.CAUTION,   50)
    };
}

/// <summary>優先度文字列をバッジ前景色ブラシに変換するコンバーター。</summary>
public class PriorityToFgConverter : OneWayConverter {
    /// <summary>優先度に対応する前景色ブラシを返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "高" => AppPalette.Brush(AppPalette.DANGER),
        "低" => AppPalette.Brush(AppPalette.COOL_BLUE),
        _    => AppPalette.Brush(AppPalette.CAUTION)
    };
}

/// <summary>ステータス文字列をバッジ背景色ブラシに変換するコンバーター。</summary>
public class StatusToBgConverter : OneWayConverter {
    /// <summary>ステータスに対応する背景色ブラシを返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "完了"       => AppPalette.Brush(AppPalette.SUCCESS, 40),
        "対応中"     => AppPalette.Brush(AppPalette.INFO,    40),
        "レビュー中" => AppPalette.Brush(AppPalette.WARNING, 40),
        _            => AppPalette.Brush(AppPalette.MUTED,   40)
    };
}

/// <summary>ステータス文字列をバッジ前景色ブラシに変換するコンバーター。</summary>
public class StatusToFgConverter : OneWayConverter {
    /// <summary>ステータスに対応する前景色ブラシを返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c) => (v as string) switch {
        "完了"       => AppPalette.Brush(AppPalette.SUCCESS),
        "対応中"     => AppPalette.Brush(AppPalette.INFO),
        "レビュー中" => AppPalette.Brush(AppPalette.WARNING),
        _            => AppPalette.Brush(AppPalette.NEUTRAL)
    };
}

/// <summary>bool 値をフォルダ/ファイル種別文字列に変換するコンバーター。</summary>
public class BoolToTypeConverter : OneWayConverter {
    /// <summary>true なら "フォルダ"、false なら "ファイル" を返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c)
        => v is bool b && b ? "フォルダ" : "ファイル";
}

/// <summary>16進数カラー文字列を Color 値に変換するコンバーター。</summary>
public class StringToColorConverter : OneWayConverter {
    /// <summary>カラー文字列を Color に変換する。変換失敗時は INFO カラーを返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c) {
        try {
            var s = (v as string ?? "#2383E2");
            if (!s.StartsWith('#')) s = "#" + s;
            return (Color)ColorConverter.ConvertFromString(s);
        }
        catch { return AppPalette.INFO; }
    }
}

/// <summary>int 値が正のとき Visible、それ以外は Collapsed を返すコンバーター。</summary>
public class IntToVisibilityConverter : OneWayConverter {
    /// <summary>値が 0 より大きければ Visible、それ以外は Collapsed を返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c)
        => v is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>AlertLevel をボーダー色ブラシに変換するコンバーター。</summary>
public class AlertLevelToBorderConverter : OneWayConverter {
    /// <summary>AlertLevel に対応するボーダー色ブラシを返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c) =>
        v is AlertLevel lv ? lv switch {
            AlertLevel.Overdue    => AppPalette.Brush(AppPalette.DANGER,  180),
            AlertLevel.DueSoon    => AppPalette.Brush(AppPalette.WARNING, 180),
            AlertLevel.NotStarted => AppPalette.Brush(AppPalette.CAUTION, 180),
            _                     => AppPalette.Brush(AppPalette.DARK)
        } : (object)AppPalette.Brush(AppPalette.DARK);
}

/// <summary>AlertLevel を背景色ブラシに変換するコンバーター。</summary>
public class AlertLevelToBgConverter : OneWayConverter {
    /// <summary>AlertLevel に対応する背景色ブラシを返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c) =>
        v is AlertLevel lv ? lv switch {
            AlertLevel.Overdue    => AppPalette.Brush(AppPalette.DANGER,  50),
            AlertLevel.DueSoon    => AppPalette.Brush(AppPalette.WARNING, 50),
            AlertLevel.NotStarted => AppPalette.Brush(AppPalette.CAUTION, 50),
            _                     => AppPalette.Brush(AppPalette.MUTED,   40)
        } : (object)Brushes.Transparent;
}

/// <summary>AlertLevel を前景色ブラシに変換するコンバーター。</summary>
public class AlertLevelToFgConverter : OneWayConverter {
    /// <summary>AlertLevel に対応する前景色ブラシを返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c) =>
        v is AlertLevel lv ? lv switch {
            AlertLevel.Overdue    => AppPalette.Brush(AppPalette.DANGER),
            AlertLevel.DueSoon    => AppPalette.Brush(AppPalette.WARNING),
            AlertLevel.NotStarted => AppPalette.Brush(AppPalette.CAUTION),
            _                     => AppPalette.Brush(AppPalette.NEUTRAL)
        } : (object)Brushes.Gray;
}

/// <summary>パーセント値を最大幅に対するピクセル幅に変換するコンバーター。</summary>
public class PercentToWidthConverter : OneWayConverter {
    /// <summary>パーセント値と最大幅パラメーターからピクセル幅を計算して返す。</summary>
    public override object Convert(object v, Type t, object p, CultureInfo c) {
        if (v is double pct && p is string ps && double.TryParse(ps, out var maxW))
            return Math.Max(0, Math.Min(pct / 100.0 * maxW, maxW));
        return 0.0;
    }
}

/// <summary>遅延日数を色ブラシに変換するコンバーター。</summary>
public class DelayToColorConverter : OneWayConverter {
    /// <summary>正数なら危険色、負数なら成功色、0 なら中立色のブラシを返す。</summary>
    public override object Convert(object value, Type t, object p, CultureInfo c) {
        if (value is int days)
            return days > 0 ? AppPalette.Brush(AppPalette.DANGER)
                 : days < 0 ? AppPalette.Brush(AppPalette.SUCCESS)
                 :             AppPalette.Brush(AppPalette.NEUTRAL);
        return AppPalette.Brush(AppPalette.NEUTRAL);
    }
}

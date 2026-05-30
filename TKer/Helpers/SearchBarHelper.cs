using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace TKer.Helpers;

/// <summary>
/// 検索バーの表示・非表示アニメーションを管理するヘルパー。
/// </summary>
public static class SearchBarHelper
{
    /// <summary>検索バーの表示状態を切り替える。</summary>
    public static void Toggle(FrameworkElement section, UIElement? focusTarget = null)
    {
        if (section.Visibility == Visibility.Collapsed)
            Open(section, focusTarget);
        else
            Close(section);
    }

    /// <summary>検索バーをアニメーションで展開して表示する。</summary>
    public static void Open(FrameworkElement section, UIElement? focusTarget = null)
    {
        section.Visibility = Visibility.Visible;
        section.BeginAnimation(FrameworkElement.HeightProperty, null);
        section.Height = double.NaN;
        section.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // DesiredSize はマージンを含むため、Height プロパティ用に上下マージンを除外する
        double measured = section.DesiredSize.Height - section.Margin.Top - section.Margin.Bottom;
        double targetH = measured > 0 ? measured : 50;
        section.Height = 0;

        var anim = new DoubleAnimation(0, targetH, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) =>
        {
            section.BeginAnimation(FrameworkElement.HeightProperty, null);
            section.Height = double.NaN;
        };
        section.BeginAnimation(FrameworkElement.HeightProperty, anim);

        focusTarget?.Focus();
    }

    /// <summary>検索バーをアニメーションで折りたたんで非表示にする。</summary>
    public static void Close(FrameworkElement section)
    {
        double currentH = section.ActualHeight > 0 ? section.ActualHeight : 50;

        var anim = new DoubleAnimation(currentH, 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            section.BeginAnimation(FrameworkElement.HeightProperty, null);
            section.Visibility = Visibility.Collapsed;
        };
        section.BeginAnimation(FrameworkElement.HeightProperty, anim);
    }
}

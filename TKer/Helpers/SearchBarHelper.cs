using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace TKer.Helpers;

public static class SearchBarHelper
{
    public static void Toggle(FrameworkElement section, UIElement? focusTarget = null)
    {
        if (section.Visibility == Visibility.Collapsed)
            Open(section, focusTarget);
        else
            Close(section);
    }

    public static void Open(FrameworkElement section, UIElement? focusTarget = null)
    {
        section.Visibility = Visibility.Visible;
        section.BeginAnimation(FrameworkElement.HeightProperty, null);
        section.Height = double.NaN;
        section.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double targetH = section.DesiredSize.Height > 0 ? section.DesiredSize.Height : 50;
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

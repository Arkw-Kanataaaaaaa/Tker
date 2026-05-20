using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Models;

namespace TKer.Views.Dialogs;

public partial class ComponentThemeDialog : Window
{
    public SectionTheme Result { get; private set; } = new();

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

    private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LblOpacity != null) LblOpacity.Text = $"{(int)e.NewValue}%";
    }

    private void UpdateBgPreview()
    {
        var c = TryParseColor(TxtBgColor.Text);
        BgPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
    }

    private void UpdateTextPreview()
    {
        var c = TryParseColor(TxtTextColor.Text);
        TextPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
    }

    private void UpdateBorderPreview()
    {
        var c = TryParseColor(TxtBorderColor.Text);
        BorderPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
    }

    private static Color? TryParseColor(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return (Color)ColorConverter.ConvertFromString(s); }
        catch { return null; }
    }

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

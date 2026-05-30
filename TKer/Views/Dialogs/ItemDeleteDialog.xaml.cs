using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TKer.Views.Dialogs;

/// <summary>アイテム削除確認ダイアログ（単一・複数対応）。</summary>
public partial class ItemDeleteDialog : Window
{
    /// <summary>削除対象アイテムと、紐づく実ファイルパス（無ければ null）。</summary>
    public sealed record Entry(string Name, string? FilePath);

    private sealed class FileToggle
    {
        public required string Path { get; init; }
        public required Border Track { get; init; }
        public required Border Thumb { get; init; }
        public required SolidColorBrush Brush { get; init; }
        public bool On;
        public bool Animating;
    }

    private readonly List<FileToggle> _fileToggles = new();
    private bool _masterOn;
    private bool _masterAnimating;
    private readonly SolidColorBrush _masterBrush = new(Color.FromRgb(80, 80, 80));

    private static readonly Color OffColor = Color.FromRgb(80, 80, 80);
    private static readonly Color OnColor  = Color.FromRgb(35, 131, 226);

    /// <summary>削除すると選択された（トグルON）ファイルパスの一覧。</summary>
    public IReadOnlyList<string> FilePathsToDelete =>
        _fileToggles.Where(t => t.On).Select(t => t.Path).ToList();

    /// <summary>単一アイテムの削除確認。</summary>
    public ItemDeleteDialog(string? filePath)
        : this(new[] { new Entry("", filePath) }) { }

    /// <summary>複数アイテムの削除確認。</summary>
    public ItemDeleteDialog(IReadOnlyList<Entry> entries)
    {
        InitializeComponent();

        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));

        ItemNameText.Text = "削除確認";
        MessageText.Text  = entries.Count <= 1
            ? "このアイテムを削除しますか？"
            : $"選択した {entries.Count} 件のアイテムを削除しますか？";

        var withFiles = entries.Where(e => !string.IsNullOrEmpty(e.FilePath)).ToList();
        if (withFiles.Count == 0)
        {
            FileDeleteSection.Visibility = Visibility.Collapsed;
            return;
        }

        FileDeleteSection.Visibility = Visibility.Visible;
        MasterToggleTrack.Background = _masterBrush;

        // 単一アイテムのときはマスタートグルのみ（個別行は冗長なので隠す）
        bool showPerRow = withFiles.Count > 1;
        foreach (var e in withFiles)
            FileTogglesPanel.Children.Add(BuildFileToggleRow(e, showPerRow));
    }

    private UIElement BuildFileToggleRow(Entry entry, bool visible)
    {
        var brush = new SolidColorBrush(OffColor);
        var thumb = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
            Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        var track = new Border
        {
            Width = 40, Height = 22, CornerRadius = new CornerRadius(11),
            Background = brush, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Grid { Children = { thumb } },
        };

        var toggle = new FileToggle { Path = entry.FilePath!, Track = track, Thumb = thumb, Brush = brush };
        _fileToggles.Add(toggle);
        track.MouseLeftButtonUp += (_, _) => SetToggle(toggle, !toggle.On, animate: true);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        if (!string.IsNullOrWhiteSpace(entry.Name))
            texts.Children.Add(new TextBlock
            {
                Text = entry.Name, FontFamily = new FontFamily("Yu Gothic UI"), FontSize = 12,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        texts.Children.Add(new TextBlock
        {
            Text = entry.FilePath, FontFamily = new FontFamily("Consolas"), FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(track, Dock.Right);
        dp.Children.Add(track);
        dp.Children.Add(texts);

        return new Border
        {
            Visibility = visible ? Visibility.Visible : Visibility.Collapsed,
            Child      = dp,
        };
    }

    private void SetToggle(FileToggle t, bool on, bool animate)
    {
        if (t.On == on) return;
        t.On = on;
        t.Animating = true;

        var thumbAnim = new ThicknessAnimation
        {
            To = on ? new Thickness(20, 0, 0, 0) : new Thickness(2, 0, 0, 0),
            Duration = TimeSpan.FromMilliseconds(animate ? 160 : 0),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        thumbAnim.Completed += (_, _) => t.Animating = false;
        t.Thumb.BeginAnimation(MarginProperty, thumbAnim);

        t.Brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            To = on ? OnColor : OffColor,
            Duration = TimeSpan.FromMilliseconds(animate ? 160 : 0),
        });

        SyncMasterToToggles();
    }

    private void ToggleMaster_Click(object sender, MouseButtonEventArgs e)
    {
        if (_masterAnimating) return;
        bool target = !_masterOn;
        SetMaster(target);
        foreach (var t in _fileToggles) SetToggle(t, target, animate: true);
    }

    private void SetMaster(bool on)
    {
        _masterOn = on;
        _masterAnimating = true;
        var anim = new ThicknessAnimation
        {
            To = on ? new Thickness(22, 0, 0, 0) : new Thickness(2, 0, 0, 0),
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) => _masterAnimating = false;
        MasterToggleThumb.BeginAnimation(MarginProperty, anim);
        _masterBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            To = on ? OnColor : OffColor, Duration = TimeSpan.FromMilliseconds(160),
        });
    }

    /// <summary>個別トグルの状態に合わせてマスタートグルの見た目を同期する。</summary>
    private void SyncMasterToToggles()
    {
        bool all = _fileToggles.Count > 0 && _fileToggles.All(t => t.On);
        if (all != _masterOn && !_masterAnimating) SetMaster(all);
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

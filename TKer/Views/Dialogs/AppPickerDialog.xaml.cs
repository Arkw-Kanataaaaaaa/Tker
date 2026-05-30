using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TKer.Helpers;

namespace TKer.Views.Dialogs;

/// <summary>起動中アプリの選択またはファイル参照によりアプリの実行ファイルを選択するダイアログ。</summary>
public partial class AppPickerDialog : Window
{
    /// <summary>選択された実行ファイルのフルパス（キャンセル時は null）。</summary>
    public string? SelectedExePath { get; private set; }

    /// <summary>起動中アプリ一覧を読み込んでダイアログを初期化する。</summary>
    public AppPickerDialog()
    {
        InitializeComponent();
        LoadRunning();
    }

    /// <summary>現在表示中のウィンドウから一意の実行ファイル一覧を抽出してリストに追加する。</summary>
    private void LoadRunning()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in Win32Window.EnumerateVisibleWindows())
        {
            if (string.IsNullOrEmpty(w.ExePath)) continue;
            if (!seen.Add(w.ExePath)) continue;

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = TryGetExeIcon(w.ExePath);
            if (icon != null)
                sp.Children.Add(new Image
                {
                    Width = 24, Height = 24, Source = icon,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

            sp.Children.Add(new TextBlock
            {
                Text       = $"{Path.GetFileName(w.ExePath)} — {w.Title}",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 420
            });

            RunningList.Items.Add(new ListBoxItem
            {
                Tag     = w.ExePath,
                Content = sp,
                Padding = new Thickness(8, 6, 8, 6)
            });
        }
    }

    /// <summary>指定実行ファイルからアイコンを抽出して WPF 用 BitmapSource に変換する。</summary>
    public static BitmapSource? TryGetExeIcon(string exePath)
    {
        try
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    private void RunningList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RunningList.SelectedItem is ListBoxItem item && item.Tag is string path)
        {
            SelectedExePath = path;
            DialogResult    = true;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "実行ファイル (*.exe)|*.exe|すべて (*.*)|*.*",
            Title  = "アプリの実行ファイルを選択"
        };
        if (ofd.ShowDialog(this) == true)
        {
            SelectedExePath = ofd.FileName;
            DialogResult    = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

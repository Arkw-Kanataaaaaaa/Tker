using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TKer.Views.Dialogs;

/// <summary>アイテム削除確認ダイアログ。</summary>
public partial class ItemDeleteDialog : Window
{
    private bool _deleteFile = false;
    private bool _isAnimating = false;
    private readonly SolidColorBrush _trackBrush = new(Color.FromRgb(80, 80, 80));

    public bool DeleteFile => _deleteFile;

    public ItemDeleteDialog(string itemName, string? filePath)
    {
        InitializeComponent();

        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));

        var label = string.IsNullOrWhiteSpace(itemName) ? "このアイテム" : $"「{itemName}」";
        ItemNameText.Text      = $"{label} 削除確認";
        ToggleTrack.Background = _trackBrush;

        if (!string.IsNullOrEmpty(filePath))
        {
            FileDeletePanel.Visibility = Visibility.Visible;
            FilePathText.Text          = filePath;
        }
    }

    private void ToggleDeleteFile_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isAnimating) return;
        _deleteFile  = !_deleteFile;
        _isAnimating = true;

        var thumbAnim = new ThicknessAnimation
        {
            To             = _deleteFile ? new Thickness(22, 0, 0, 0) : new Thickness(2, 0, 0, 0),
            Duration       = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        thumbAnim.Completed += (_, _) => _isAnimating = false;
        ToggleThumb.BeginAnimation(MarginProperty, thumbAnim);

        var colorAnim = new ColorAnimation
        {
            To       = _deleteFile ? Color.FromRgb(35, 131, 226) : Color.FromRgb(80, 80, 80),
            Duration = TimeSpan.FromMilliseconds(180)
        };
        _trackBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TKer.Views.Dialogs;

/// <summary>プロジェクト削除確認ダイアログ。</summary>
public partial class ProjectDeleteDialog : Window
{
    private bool _deleteFolder = false;
    private bool _isAnimating  = false;
    private readonly SolidColorBrush _trackBrush = new(Color.FromRgb(80, 80, 80));

    public bool DeleteFolder => _deleteFolder;

    public ProjectDeleteDialog(string projectName, string? folderPath, bool isFolderManaged)
    {
        InitializeComponent();

        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));

        ProjectNameText.Text   = $"「{projectName}」";
        ToggleTrack.Background = _trackBrush;

        if (isFolderManaged && !string.IsNullOrEmpty(folderPath))
        {
            FolderDeletePanel.Visibility = Visibility.Visible;
            FolderPathText.Text          = folderPath;
        }
    }

    private void ToggleDeleteFolder_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isAnimating) return;
        _deleteFolder  = !_deleteFolder;
        _isAnimating   = true;

        var thumbAnim = new ThicknessAnimation
        {
            To             = _deleteFolder ? new Thickness(22, 0, 0, 0) : new Thickness(2, 0, 0, 0),
            Duration       = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        thumbAnim.Completed += (_, _) => _isAnimating = false;
        ToggleThumb.BeginAnimation(MarginProperty, thumbAnim);

        var colorAnim = new ColorAnimation
        {
            To       = _deleteFolder ? Color.FromRgb(35, 131, 226) : Color.FromRgb(80, 80, 80),
            Duration = TimeSpan.FromMilliseconds(180)
        };
        _trackBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)  => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e)  => DialogResult = false;
}

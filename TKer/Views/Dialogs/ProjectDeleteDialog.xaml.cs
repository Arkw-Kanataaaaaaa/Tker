using System.Windows;

namespace TKer.Views.Dialogs;

/// <summary>プロジェクト削除確認ダイアログ。</summary>
public partial class ProjectDeleteDialog : Window
{
    public bool DeleteFolder => ChkDeleteFolder.IsChecked == true;

    public ProjectDeleteDialog(string projectName, string? folderPath, bool isFolderManaged)
    {
        InitializeComponent();

        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));

        ProjectNameText.Text = $"「{projectName}」";

        if (isFolderManaged && !string.IsNullOrEmpty(folderPath))
        {
            FolderDeletePanel.Visibility = Visibility.Visible;
            FolderPathText.Text          = folderPath;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)  => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e)  => DialogResult = false;
}

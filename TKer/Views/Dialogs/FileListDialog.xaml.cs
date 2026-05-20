using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;

namespace TKer.Views.Dialogs;

public partial class FileListDialog : Window
{
    private readonly TaskItem _task;
    private readonly ProjectService _svc;

    public FileListDialog(TaskItem task, ProjectService svc)
    {
        _task = task;
        _svc = svc;
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { Close(); }));
        TitleBlock.Text = $"📁 {task.FolderName}";
        PathBlock.Text = task.FolderPath;
        LoadFiles();
    }

    private void LoadFiles()
    {
        var node = _svc.GetTaskFolderTree(_task);
        if (node == null)
        {
            FileList.ItemsSource = null;
            return;
        }
        FileList.ItemsSource = node.Children;
    }

    private void FileList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not FileNode node) return;
        if (node.IsDirectory)
            ShellHelper.OpenInExplorer(node.FullPath);
        else
            Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_task.FolderPath))
            ShellHelper.OpenInExplorer(_task.FolderPath);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;

namespace TKer.Views.Dialogs;

/// <summary>タスクフォルダ内のファイルツリーを表示するダイアログ。</summary>
public partial class FileListDialog : Window
{
    private readonly TaskItem _task;
    private readonly ProjectService _svc;

    /// <summary>タスクとサービスを受け取りファイル一覧を読み込んで初期化する。</summary>
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

    /// <summary>タスクフォルダのツリー構造を取得してリストに設定する。</summary>
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

    /// <summary>ファイルノードをダブルクリックしてファイルまたはフォルダを開く。</summary>
    private void FileList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not FileNode node) return;
        if (node.IsDirectory)
            ShellHelper.OpenInExplorer(node.FullPath);
        else
            Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true });
    }

    /// <summary>タスクフォルダをエクスプローラーで開く。</summary>
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_task.FolderPath))
            ShellHelper.OpenInExplorer(_task.FolderPath);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

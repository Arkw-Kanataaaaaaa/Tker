using System.Windows;
using TKer.Models;

namespace TKer.Views.Dialogs;

/// <summary>プロジェクト名・説明・バージョン・担当者を編集するダイアログ。</summary>
public partial class ProjectSettingsDialog : Window
{
    public string ProjectName { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Version     { get; private set; } = "";
    public string Manager     { get; private set; } = "";

    /// <summary>既存プロジェクトデータをフォームに反映して初期化する。</summary>
    public ProjectSettingsDialog(ProjectData project)
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));
        TxtName.Text        = project.Settings.ProjectName;
        TxtDescription.Text = project.Settings.Description;
        TxtVersion.Text     = project.ProjectVersion;
        TxtManager.Text     = project.Manager;
    }

    /// <summary>入力値を検証してプロパティに反映しダイアログを確定する。</summary>
    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        { MessageBox.Show("プロジェクト名を入力してください"); return; }
        ProjectName = TxtName.Text;
        Description = TxtDescription.Text;
        Version     = TxtVersion.Text;
        Manager     = TxtManager.Text;
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

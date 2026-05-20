using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TKer.Helpers;
using TKer.ViewModels;

namespace TKer.Views.Pages;

/// <summary>プロジェクトの新規作成・既存読み込み・設定編集・Excelインポートを行うページ。</summary>
public partial class SetupPage : Page
{
    private readonly MainViewModel _vm;

    /// <summary>セットアップページを初期化して現在プロジェクト情報を表示する。</summary>
    public SetupPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Loaded += (_, _) => RefreshCurrentProject();
    }

    /// <summary>ベースフォルダー選択ダイアログを開いてパスを設定する。</summary>
    private void BrowseBasePath_Click(object sender, RoutedEventArgs e)
    {
        var picked = FolderPicker.Pick();
if (picked != null) TxtBasePath.Text = picked;
    }

    /// <summary>入力内容を検証して新規プロジェクトを作成しダッシュボードに遷移する。</summary>
    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtBasePath.Text))
        { MessageBox.Show("フォルダを選択してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(TxtProjectName.Text))
        { MessageBox.Show("プロジェクト名を入力してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        try
        {
            _vm.ProjectService.CreateProject(TxtBasePath.Text, TxtProjectName.Text, TxtDescription.Text);
            _vm.NavigateToCommand.Execute("Dashboard");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"作成エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>ファイルダイアログで既存プロジェクトファイルを選択して読み込む。</summary>
    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "project_data.json を選択",
            Filter = "TKer データ (project_data.json)|project_data.json|JSONファイル (*.json)|*.json"
        };
        if (dialog.ShowDialog() == true)
        {
            if (_vm.ProjectService.LoadProjectWithAutoRemap(dialog.FileName))
                _vm.NavigateToCommand.Execute("Dashboard");
            else
                MessageBox.Show("プロジェクトファイルの読み込みに失敗しました", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    /// <summary>プロジェクト設定ダイアログを開いて名前・説明などを更新する。</summary>
    private void EditProjectSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsProjectLoaded)
        { MessageBox.Show("プロジェクトを開いてください"); return; }
        var dlg = new TKer.Views.Dialogs.ProjectSettingsDialog(
            _vm.ProjectService.CurrentProject!) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            _vm.ProjectService.UpdateProjectSettings(
                dlg.ProjectName, dlg.Description, dlg.Version, dlg.Manager);
            MessageBox.Show("設定を保存しました", "完了",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    /// <summary>現在読み込まれているプロジェクトの情報をバナーに表示する。</summary>
    private void RefreshCurrentProject()
    {
        if (_vm.IsProjectLoaded && _vm.ProjectService.CurrentProject != null)
        {
            CurrentProjectBanner.Visibility = System.Windows.Visibility.Visible;
            TxtCurrentProject.Text = _vm.ProjectService.CurrentProject.Settings.ProjectPath;
        }
        else
        {
            CurrentProjectBanner.Visibility = System.Windows.Visibility.Collapsed;
        }
    }

    /// <summary>Excelファイルを選択してプロジェクトデータにインポートする。</summary>
    private void ImportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsProjectLoaded)
        { MessageBox.Show("先にプロジェクトを作成または開いてください", "未設定", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var dialog = new OpenFileDialog
        {
            Title = "インポートするExcelファイルを選択",
            Filter = "Excelファイル (*.xlsx;*.xls)|*.xlsx;*.xls"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                _vm.ProjectService.ImportFromExcel(dialog.FileName);
                MessageBox.Show("インポートが完了しました", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                _vm.NavigateToCommand.Execute("TaskList");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"インポートエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

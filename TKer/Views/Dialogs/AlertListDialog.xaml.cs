using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;

namespace TKer.Views.Dialogs;

/// <summary>アラート一覧を表示するダイアログ。</summary>
public partial class AlertListDialog : Window
{
    private readonly AppSettingsService _svc;
    private readonly MainViewModel _vm;
    private List<AlertItem> _allAlerts = new();
    private string _filter = "All";

    /// <summary>アラート一覧ダイアログを初期化し、アラートを読み込む。</summary>
    public AlertListDialog(AppSettingsService svc, MainViewModel vm)
    {
        _svc = svc;
        _vm  = vm;
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { Close(); }));
        Load();
    }

    /// <summary>サービスからアラートを収集してフィルターを適用する。</summary>
    private void Load()
    {
        _allAlerts = _svc.CollectAlerts();
        ApplyFilter();
    }

    /// <summary>現在のフィルター条件に基づいてアラートを絞り込みグリッドに反映する。</summary>
    private void ApplyFilter()
    {
        var filtered = _filter switch
        {
            "Overdue"    => _allAlerts.Where(a => a.Level == AlertLevel.Overdue).ToList(),
            "DueSoon"    => _allAlerts.Where(a => a.Level == AlertLevel.DueSoon).ToList(),
            "NotStarted" => _allAlerts.Where(a => a.Level == AlertLevel.NotStarted).ToList(),
            _            => _allAlerts
        };
        AlertGrid.ItemsSource = filtered;
        CountLabel.Text = $"{filtered.Count} 件";
    }

    /// <summary>フィルターボタンのクリックでフィルター種別を切り替える。</summary>
    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _filter = ((Button)sender).Tag as string ?? "All";
        ApplyFilter();
    }

    /// <summary>グリッドのダブルクリックでプロジェクトを開く。</summary>
    private void AlertGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenProject_Click(sender, e);
    }

    /// <summary>選択したアラートのプロジェクトを開いてタスク一覧へ遷移する。</summary>
    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (AlertGrid.SelectedItem is AlertItem alert)
        {
            _vm.SwitchProjectCommand.Execute(alert.DataFilePath);
            _vm.NavigateToCommand.Execute("TaskList");
            Close();
        }
        else
        {
            MessageBox.Show("アラート行を選択してください", "未選択",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

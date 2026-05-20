using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;

namespace TKer.Views.Dialogs;

public partial class AlertListDialog : Window
{
    private readonly AppSettingsService _svc;
    private readonly MainViewModel _vm;
    private List<AlertItem> _allAlerts = new();
    private string _filter = "All";

    public AlertListDialog(AppSettingsService svc, MainViewModel vm)
    {
        _svc = svc;
        _vm  = vm;
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { Close(); }));
        Load();
    }

    private void Load()
    {
        _allAlerts = _svc.CollectAlerts();
        ApplyFilter();
    }

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

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _filter = ((Button)sender).Tag as string ?? "All";
        ApplyFilter();
    }

    private void AlertGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenProject_Click(sender, e);
    }

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

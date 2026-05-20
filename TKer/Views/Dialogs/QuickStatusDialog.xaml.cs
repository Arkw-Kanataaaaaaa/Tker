using System.Windows;
using System.Windows.Controls;
using TKer.Models;

namespace TKer.Views.Dialogs;

/// <summary>タスクのステータスとメモを素早く更新するダイアログ。</summary>
public partial class QuickStatusDialog : Window
{
    public string NewStatus { get; private set; } = "";
    public string NewNotes  { get; private set; } = "";

    /// <summary>対象タスクの現在ステータスをコンボボックスに反映して初期化する。</summary>
    public QuickStatusDialog(TaskItem task)
    {
        InitializeComponent();
        TxtTaskName.Text = task.Name;

        // 現在のステータスを選択
        foreach (ComboBoxItem item in CmbStatus.Items)
        {
            if (item.Tag?.ToString() == task.Status)
            {
                CmbStatus.SelectedItem = item;
                break;
            }
        }
        if (CmbStatus.SelectedIndex < 0) CmbStatus.SelectedIndex = 0;

        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => DialogResult = false));
    }

    /// <summary>選択されたステータスとメモをプロパティに設定してダイアログを確定する。</summary>
    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        NewStatus = (CmbStatus.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        NewNotes  = TxtNote.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

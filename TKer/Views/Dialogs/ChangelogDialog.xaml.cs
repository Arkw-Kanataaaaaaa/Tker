using System.Windows;
namespace TKer.Views.Dialogs;

/// <summary>アプリの変更履歴を表示するダイアログ。</summary>
public partial class ChangelogDialog : Window
{
    /// <summary>変更履歴ダイアログを初期化する。</summary>
    public ChangelogDialog()
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { Close(); }));
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

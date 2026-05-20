using System.Windows;
namespace TKer.Views.Dialogs;
public partial class ChangelogDialog : Window
{
    public ChangelogDialog()
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { Close(); }));
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

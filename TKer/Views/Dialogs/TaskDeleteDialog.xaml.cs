using System.Windows;

namespace TKer.Views.Dialogs;

public partial class TaskDeleteDialog : Window
{
    public bool DeleteFolder { get; private set; } = false;

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    public TaskDeleteDialog(string taskName)
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));
        TaskNameBlock.Text = taskName;
    }

    private void DeleteDataOnly_Click(object sender, RoutedEventArgs e)
    {
        DeleteFolder = false;
        DialogResult = true;
    }

    private void DeleteWithFolder_Click(object sender, RoutedEventArgs e)
    {
        DeleteFolder = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}

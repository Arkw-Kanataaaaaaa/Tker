using System.Windows;

namespace TKer.Views.Dialogs;

/// <summary>タスクのデータのみ削除するかフォルダごと削除するかを選択するダイアログ。</summary>
public partial class TaskDeleteDialog : Window
{
    public bool DeleteFolder { get; private set; } = false;

    /// <summary>Escキーでダイアログをキャンセル閉じする。</summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    /// <summary>削除対象タスク名を表示して初期化する。</summary>
    public TaskDeleteDialog(string taskName)
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));
        TaskNameBlock.Text = taskName;
    }

    /// <summary>データのみ削除を選択してダイアログを確定する。</summary>
    private void DeleteDataOnly_Click(object sender, RoutedEventArgs e)
    {
        DeleteFolder = false;
        DialogResult = true;
    }

    /// <summary>フォルダごと削除を選択してダイアログを確定する。</summary>
    private void DeleteWithFolder_Click(object sender, RoutedEventArgs e)
    {
        DeleteFolder = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}

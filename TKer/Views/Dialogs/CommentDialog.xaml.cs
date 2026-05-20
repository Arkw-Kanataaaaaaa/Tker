using System.Windows;
using TKer.Models;
using TKer.Services;

namespace TKer.Views.Dialogs;

public partial class CommentDialog : Window
{
    private readonly TaskItem      _task;
    private readonly ProjectService _svc;

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    public CommentDialog(TaskItem task, ProjectService svc)
    {
        _task = task;
        _svc  = svc;
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { Close(); }));
        TitleBlock.Text = $"💬 {task.Name}";
        CommentList.ItemsSource = task.Comments;
    }

    private void Post_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtComment.Text)) return;
        _svc.AddComment(_task.Id,
            string.IsNullOrWhiteSpace(TxtAuthor.Text) ? "匿名" : TxtAuthor.Text,
            TxtComment.Text);
        TxtComment.Clear();
        CommentList.ItemsSource = null;
        CommentList.ItemsSource = _task.Comments;
        CommentList.ScrollIntoView(_task.Comments[^1]);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

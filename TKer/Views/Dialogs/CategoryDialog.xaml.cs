using System.Windows;
using TKer.Models;

namespace TKer.Views.Dialogs;

public partial class CategoryDialog : Window
{
    public string CategoryName { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Color { get; private set; } = "#3D7EFF";
    public bool RenameFolder { get; private set; } = false;

    public CategoryDialog(Category? existing)
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));
        if (existing != null)
        {
            TxtName.Text = existing.Name;
            TxtDescription.Text = existing.Description;
            TxtColor.Text = existing.Color;
            Title = "カテゴリー編集";
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        { System.Windows.MessageBox.Show("名前を入力してください"); return; }
        CategoryName = TxtName.Text;
        Description = TxtDescription.Text;
        Color = string.IsNullOrWhiteSpace(TxtColor.Text) ? "#3D7EFF" : TxtColor.Text;
        RenameFolder = ChkRenameFolder.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

using System.Windows;
using TKer.Models;

namespace TKer.Views.Dialogs;

/// <summary>カテゴリーの新規作成・編集を行うダイアログ。</summary>
public partial class CategoryDialog : Window
{
    public string CategoryName { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Color { get; private set; } = "#3D7EFF";
    public bool RenameFolder { get; private set; } = false;

    /// <summary>既存カテゴリーがある場合はその値をフォームに反映して初期化する。</summary>
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

    /// <summary>入力値を検証してプロパティに反映しダイアログを確定する。</summary>
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

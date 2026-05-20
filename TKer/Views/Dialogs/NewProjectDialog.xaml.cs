using System.IO;
using System.Windows;

namespace TKer.Views.Dialogs;

/// <summary>新規プロジェクトの名前・説明・保存先を入力するダイアログ。</summary>
public partial class NewProjectDialog : Window
{
    public string ProjectName { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string SavePath    { get; private set; } = "";

    /// <summary>新規プロジェクトダイアログを初期化し名前入力欄にフォーカスを当てる。</summary>
    public NewProjectDialog()
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));
        Loaded += (_, _) => TxtName.Focus();
    }

    /// <summary>SaveFileDialogを利用して保存先フォルダパスを選択する。</summary>
    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title           = "保存先フォルダを選択（そのままOKを押してください）",
            ValidateNames   = false,
            CheckFileExists = false,
            FileName        = "ここを変更せずOKを押してください",
            Filter          = "フォルダ|*.none"
        };
        if (dlg.ShowDialog() == true)
            TxtPath.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
    }

    /// <summary>入力値を検証してプロパティに設定しダイアログを確定する。</summary>
    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("プロジェクト名を入力してください", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(TxtPath.Text))
        {
            MessageBox.Show("保存先フォルダを選択してください", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProjectName = TxtName.Text.Trim();
        Description = TxtDesc.Text.Trim();
        SavePath    = TxtPath.Text.Trim();
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

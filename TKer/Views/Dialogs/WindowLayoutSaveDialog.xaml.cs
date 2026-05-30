using System.Windows;

namespace TKer.Views.Dialogs;

/// <summary>ウィンドウレイアウト保存・編集時に名前と説明を入力するダイアログ。</summary>
public partial class WindowLayoutSaveDialog : Window
{
    /// <summary>入力された名前（前後空白除去後）。</summary>
    public string LayoutName        => TxtName.Text.Trim();
    /// <summary>入力された説明（前後空白除去後）。</summary>
    public string LayoutDescription => TxtDescription.Text.Trim();

    /// <summary>初期値（編集時用）を受け取って初期化する。</summary>
    public WindowLayoutSaveDialog(string initialName = "", string initialDescription = "")
    {
        InitializeComponent();
        TxtName.Text        = initialName;
        TxtDescription.Text = initialDescription;
        Loaded += (_, _) => { TxtName.Focus(); TxtName.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LayoutName))
        {
            MessageBox.Show(this, "名前を入力してください。",
                "入力チェック", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

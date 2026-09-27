using System.Windows;
using TKer.Views;

namespace TKer;

/// <summary>アプリケーションのエントリポイント。</summary>
public partial class App : Application
{
    /// <summary>メインウィンドウを表示する。</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, ex) =>
        {
            MessageBox.Show($"予期しないエラーが発生しました:\n{ex.Exception.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}

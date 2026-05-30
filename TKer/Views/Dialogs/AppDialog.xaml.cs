using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TKer.Views.Dialogs;

public enum AppDialogType { Info, Warning, Error, Confirm }

/// <summary>情報・警告・エラー・確認用の汎用アプリダイアログ。</summary>
public partial class AppDialog : Window
{
    public bool Confirmed { get; private set; } = false;

    /// <summary>ダイアログの種類と内容を指定して初期化する。</summary>
    private AppDialog(string title, string message, AppDialogType type, bool showCancel,
                      string confirmLabel = "", bool dangerConfirm = false, string heading = "")
    {
        InitializeComponent();
        // × ボタン・Esc キーで閉じれるよう SystemCommands.CloseWindowCommand を登録
        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => Close()));
        Title = title;
        TitleBarText.Text = title;
        TitleText.Text = !string.IsNullOrEmpty(heading) ? heading : title;
        MessageText.Text = message;

        (IconText.Text, IconText.Foreground) = type switch
        {
            AppDialogType.Error   => ("✕", new SolidColorBrush(Color.FromRgb(224, 62, 62))),
            AppDialogType.Warning => ("⚠", new SolidColorBrush(Color.FromRgb(223, 171, 1))),
            AppDialogType.Confirm => ("❓", new SolidColorBrush(Color.FromRgb(11, 110, 153))),
            _                     => ("ℹ", new SolidColorBrush(Color.FromRgb(35, 131, 226))),
        };

        if (showCancel)
        {
            var cancel = MakeButton("キャンセル", false, isPrimary: false);
            ButtonPanel.Children.Add(cancel);
        }
        var okLabel = !string.IsNullOrEmpty(confirmLabel) ? confirmLabel : (showCancel ? "はい" : "OK");
        var ok = MakeButton(okLabel, true, isPrimary: true, dangerConfirm);
        ButtonPanel.Children.Add(ok);
    }

    /// <summary>指定ラベルと結果値でボタンを生成して返す。</summary>
    private Button MakeButton(string label, bool result, bool isPrimary, bool danger = false)
    {
        var styleKey = danger ? "DangerFilledButton" : (isPrimary ? "PrimaryButton" : "SecondaryButton");
        var btn = new Button
        {
            Content = label,
            Style = (Style)FindResource(styleKey),
            Padding = new Thickness(20, 6, 20, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        btn.Click += (_, _) => { Confirmed = result; DialogResult = result; };
        return btn;
    }

    // ── 静的ヘルパー ────────────────────────────────
    public static void ShowInfo(string message, string title = "情報", Window? owner = null)
        => Show(title, message, AppDialogType.Info, false, owner);

    public static void ShowError(string message, string title = "エラー", Window? owner = null)
        => Show(title, message, AppDialogType.Error, false, owner);

    public static void ShowWarning(string message, string title = "警告", Window? owner = null)
        => Show(title, message, AppDialogType.Warning, false, owner);

    /// <summary>確認ダイアログを表示し、ユーザーの選択結果を返す。</summary>
    public static bool Confirm(string message, string title = "確認", Window? owner = null,
                               string confirmLabel = "", bool dangerConfirm = false,
                               string heading = "")
    {
        var dlg = new AppDialog(title, message, AppDialogType.Confirm, showCancel: true,
                                confirmLabel, dangerConfirm, heading);
        if (owner != null) dlg.Owner = owner;
        dlg.ShowDialog();
        return dlg.Confirmed;
    }

    /// <summary>指定種別のダイアログを表示する内部メソッド。</summary>
    private static void Show(string title, string message, AppDialogType type, bool showCancel, Window? owner)
    {
        var dlg = new AppDialog(title, message, type, showCancel);
        if (owner != null) dlg.Owner = owner;
        dlg.ShowDialog();
    }
}

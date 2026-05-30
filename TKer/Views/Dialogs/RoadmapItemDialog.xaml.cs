using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using TKer.Models;

namespace TKer.Views.Dialogs;

/// <summary>
/// ロードマップ項目の追加・編集用ダイアログ。
/// new RoadmapItemDialog() で新規、new RoadmapItemDialog(existing) で編集モード。
/// </summary>
public partial class RoadmapItemDialog : Window
{
    /// <summary>編集対象のロードマップ項目（新規追加時は新インスタンス）。</summary>
    public RoadmapItem Item { get; }

    /// <summary>追加モードで初期化する。</summary>
    public RoadmapItemDialog() : this(null) { }

    /// <summary>編集モード（existing 指定）で初期化する。null の場合は追加モード。</summary>
    public RoadmapItemDialog(RoadmapItem? existing)
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => Close()));

        // ステータス選択肢
        foreach (var s in RoadmapStatusValues.ALL)
            CmbStatus.Items.Add(s);

        if (existing == null)
        {
            Item = new RoadmapItem();
            HeaderTitle.Text  = "ロードマップ項目を追加";
            TitleBarText.Text = "ロードマップ項目を追加";
            BtnOk.Content     = "作成";
            CmbStatus.SelectedItem = "計画中";
        }
        else
        {
            Item = existing;
            HeaderTitle.Text  = "ロードマップ項目を編集";
            TitleBarText.Text = "ロードマップ項目を編集";
            BtnOk.Content     = "保存";
            TxtTitle.Text       = existing.Title;
            TxtVersion.Text     = existing.Version;
            TxtDescription.Text = existing.Description;
            CmbStatus.SelectedItem = existing.Status;
            TxtTargetDate.Text = existing.TargetDate?.ToString("yyyy/MM/dd") ?? "";
        }

        Loaded += (_, _) => { TxtTitle.Focus(); TxtTitle.SelectAll(); };
    }

    /// <summary>入力値を Item に反映して DialogResult=true で閉じる。</summary>
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtTitle.Text))
        {
            MessageBox.Show(this, "タイトルを入力してください。",
                "入力エラー", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtTitle.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(TxtVersion.Text))
        {
            MessageBox.Show(this, "バージョン / 期間を入力してください。",
                "入力エラー", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtVersion.Focus();
            return;
        }

        Item.Title       = TxtTitle.Text.Trim();
        Item.Version     = TxtVersion.Text.Trim();
        Item.Description = TxtDescription.Text.Trim();
        Item.Status      = CmbStatus.SelectedItem as string ?? "計画中";
        Item.TargetDate  = ParseDate(TxtTargetDate.Text);

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>yyyy/MM/dd 形式の日付文字列をパースして DateTime を返す。失敗時は null。</summary>
    private static DateTime? ParseDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParseExact(s.Trim(), new[] { "yyyy/MM/dd", "yyyy-MM-dd", "yyyy/M/d" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return null;
    }
}

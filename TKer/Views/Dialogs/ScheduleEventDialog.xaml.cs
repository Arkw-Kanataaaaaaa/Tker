using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TKer.Models;

namespace TKer.Views.Dialogs;

/// <summary>スケジュールイベントの新規作成・編集を行うダイアログ。</summary>
public partial class ScheduleEventDialog : Window
{
    // 返り値プロパティ
    public string  EventTitle       { get; private set; } = "";
    public string  EventDescription { get; private set; } = "";
    public string  EventLocation    { get; private set; } = "";
    public DateTime EventStart      { get; private set; }
    public DateTime EventEnd        { get; private set; }
    public bool    EventIsAllDay    { get; private set; }
    public string  EventColor       { get; private set; } = "#2383E2";

    /// <summary>既存イベントまたはデフォルト日付を基にフォームを初期化する。</summary>
    public ScheduleEventDialog(ScheduleEvent? existing, DateTime defaultDate)
    {
        InitializeComponent();

        // デフォルト値
        var start = existing?.StartTime ?? defaultDate.Date.AddHours(9);
        var end   = existing?.EndTime   ?? defaultDate.Date.AddHours(10);

        DpStart.SelectedDate = start.Date;
        DpEnd.SelectedDate   = end.Date;
        TxtStartTime.Text    = start.ToString("HH:mm");
        TxtEndTime.Text      = end.ToString("HH:mm");
        ChkAllDay.IsChecked  = existing?.IsAllDay ?? false;
        TxtTitle.Text        = existing?.Title       ?? "";
        TxtDesc.Text         = existing?.Description ?? "";
        TxtLocation.Text     = existing?.Location    ?? "";

        // カラー選択
        SelectColorRadio(existing?.Color ?? "#2383E2");

        UpdateTimeVisibility();

        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => DialogResult = false));

        Loaded += (_, _) => TxtTitle.Focus();
    }

    private void ChkAllDay_Changed(object sender, RoutedEventArgs e) => UpdateTimeVisibility();

    /// <summary>終日チェックの状態に応じて時刻入力パネルの表示を切り替える。</summary>
    private void UpdateTimeVisibility()
    {
        var vis = ChkAllDay.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        TimeStartPanel.Visibility = vis;
        TimeEndPanel.Visibility   = vis;
    }

    /// <summary>カラーラジオボタンの選択変更でイベントカラーを更新する。</summary>
    private void Color_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
            EventColor = rb.Tag?.ToString() ?? "#2383E2";
    }

    /// <summary>指定カラーに対応するラジオボタンを選択状態にする。</summary>
    private void SelectColorRadio(string color)
    {
        var target = color.ToUpperInvariant();
        foreach (RadioButton rb in new[] { RbBlue, RbCyan, RbGreen, RbOrange, RbRed, RbPurple })
        {
            if (rb.Tag?.ToString()?.ToUpperInvariant() == target)
            {
                rb.IsChecked = true;
                EventColor = color;
                return;
            }
        }
        RbBlue.IsChecked = true;
        EventColor = "#2383E2";
    }

    /// <summary>入力値を検証してイベントプロパティに反映しダイアログを確定する。</summary>
    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtTitle.Text))
        {
            MessageBox.Show("タイトルを入力してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtTitle.Focus();
            return;
        }

        var startDate = DpStart.SelectedDate ?? DateTime.Today;
        var endDate   = DpEnd.SelectedDate   ?? startDate;

        bool allDay = ChkAllDay.IsChecked == true;

        if (allDay)
        {
            EventStart   = startDate;
            EventEnd     = endDate;
            EventIsAllDay = true;
        }
        else
        {
            TimeSpan ts = ParseTime(TxtStartTime.Text, TimeSpan.FromHours(9));
            TimeSpan te = ParseTime(TxtEndTime.Text,   TimeSpan.FromHours(10));
            EventStart    = startDate.Add(ts);
            EventEnd      = endDate.Add(te);
            EventIsAllDay = false;

            if (EventEnd <= EventStart)
            {
                MessageBox.Show("終了日時は開始日時より後に設定してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        EventTitle       = TxtTitle.Text.Trim();
        EventDescription = TxtDesc.Text.Trim();
        EventLocation    = TxtLocation.Text.Trim();

        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>時刻文字列をTimeSpanに変換する。変換失敗時はフォールバック値を返す。</summary>
    private static TimeSpan ParseTime(string text, TimeSpan fallback)
    {
        if (TimeSpan.TryParseExact(text.Trim(), @"hh\:mm", null, out var ts)) return ts;
        if (TimeSpan.TryParseExact(text.Trim(), @"h\:mm",  null, out ts))     return ts;
        return fallback;
    }
}

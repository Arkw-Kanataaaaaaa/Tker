using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

public partial class AppSettingsPage : Page, IRefreshable
{
    private readonly MainViewModel      _vm;
    private readonly AppSettingsService _svc;
    private List<AppShortcut> _shortcuts = new();

    public AppSettingsPage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = vm.AppSettingsService;
        InitializeComponent();
        SetActiveNav(NavBtnGeneral);
    }

    public void Refresh()
    {
        TxtGraceDays.Text          = _svc.AlertNotStartedGraceDays.ToString();
        ChkHideCompleted.IsChecked = !_svc.ShowCompletedInSchedule;
        TxtBgPath.Text             = _svc.BackgroundImagePath ?? "";
        RbPersonalMode.IsChecked   = _svc.AppMode == "Personal";
        RbMultiMode.IsChecked      = _svc.AppMode != "Personal";

        ApplyBackground();

        // ── ログローテーション設定 ──
        var lr = _svc.LogRotation;
        ChkLogEnabled.IsChecked = lr.Enabled;
        foreach (ComboBoxItem item in CbLogPeriod.Items)
            if ((item.Tag as string) == lr.Period) { CbLogPeriod.SelectedItem = item; break; }
        if (CbLogPeriod.SelectedIndex < 0) CbLogPeriod.SelectedIndex = 0;
        TxtLogMaxSizeMB.Text = lr.MaxFileSizeMB.ToString();
        TxtLogMaxFiles.Text  = lr.MaxFiles.ToString();
        foreach (ComboBoxItem item in CbLogOnRotate.Items)
            if ((item.Tag as string) == lr.OnRotate) { CbLogOnRotate.SelectedItem = item; break; }
        if (CbLogOnRotate.SelectedIndex < 0) CbLogOnRotate.SelectedIndex = 0;

        _shortcuts = _svc.Shortcuts.Select(s => new AppShortcut
        {
            Id = s.Id, Name = s.Name, Icon = s.Icon, Path = s.Path
        }).ToList();
        RefreshShortcutList();
    }

    // ══════ サイドバーナビゲーション ══════

    private void NavGeneral_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavBtnGeneral);
        SecGeneral.BringIntoView();
    }

    private void NavLog_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavBtnLog);
        SecLog.BringIntoView();
    }

    private void NavShortcuts_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavBtnShortcuts);
        SecShortcuts.BringIntoView();
    }

    private void SetActiveNav(Button active)
    {
        var fgPrim = TryBrush("TextPrimaryBrush")   ?? Brushes.White;
        var fgSec  = TryBrush("TextSecondaryBrush") ?? Brushes.Gray;

        foreach (var btn in new[] { NavBtnGeneral, NavBtnLog, NavBtnShortcuts })
        {
            bool isActive = btn == active;
            btn.Background = isActive
                ? new SolidColorBrush(Color.FromArgb(40, 35, 131, 226))
                : Brushes.Transparent;
            btn.Foreground = isActive ? fgPrim : fgSec;
            btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
        }
    }

    private static Brush? TryBrush(string key)
    {
        try { return Application.Current.Resources[key] as Brush; }
        catch { return null; }
    }

    // ══════ 一般設定 ══════

    private void BrowseBg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "背景ファイルを選択",
            Filter = "画像・動画ファイル (*.png;*.jpg;*.jpeg;*.bmp;*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm)|*.png;*.jpg;*.jpeg;*.bmp;*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm|画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|動画ファイル (*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm)|*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm"
        };
        if (dlg.ShowDialog() == true) TxtBgPath.Text = dlg.FileName;
    }

    private void ClearBg_Click(object sender, RoutedEventArgs e) => TxtBgPath.Text = "";

    private void ApplyBackground()
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(_svc.AppSettingsBgColor);
            color.A = (byte)(_svc.AppSettingsBgOpacity * 255);
            ContentBorder.Background = new SolidColorBrush(color);
        }
        catch { ContentBorder.Background = Brushes.Transparent; }
    }

    // ══════ ショートカット ══════

    private void RefreshShortcutList()
    {
        ScListPanel.Children.Clear();
        var dim  = TryBrush("TextDimBrush")     ?? Brushes.Gray;
        var prim = TryBrush("TextPrimaryBrush") ?? Brushes.White;
        var bdr  = TryBrush("BorderBrush")      ?? Brushes.DarkGray;

        if (_shortcuts.Count == 0)
        {
            ScListPanel.Children.Add(new TextBlock
            {
                Text = "ショートカットがありません。「➕ 追加」から登録できます",
                FontSize = 12, Foreground = dim,
                Margin = new Thickness(4, 12, 4, 12)
            });
            return;
        }

        foreach (var sc in _shortcuts.ToList())
        {
            var row = new Border
            {
                BorderBrush = (Brush)bdr, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6, 8, 6, 8)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var iconTb = new TextBlock { Text = sc.Icon, FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(iconTb, 0); g.Children.Add(iconTb);

            var nameTb = new TextBlock { Text = sc.Name, FontSize = 13, Foreground = (Brush)prim,
                VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameTb, 1); g.Children.Add(nameTb);

            var pathTb = new TextBlock { Text = sc.Path, FontSize = 11, Foreground = (Brush)dim,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(pathTb, 2); g.Children.Add(pathTb);

            var delBtn = new Button
            {
                Content = "🗑️ 削除", Padding = new Thickness(6, 3, 6, 3),
                Style = (Style)FindResource("DangerButton"),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center
            };
            var captured = sc;
            delBtn.Click += (_, _) => { _shortcuts.Remove(captured); RefreshShortcutList(); };
            Grid.SetColumn(delBtn, 3); g.Children.Add(delBtn);

            row.Child = g;
            ScListPanel.Children.Add(row);
        }
    }

    private void AddShortcut_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "ショートカット先を選択",
            Filter = "すべてのファイル (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var defaultName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        var sc = new AppShortcut { Name = defaultName, Path = dlg.FileName, Icon = "🔗" };

        var bg  = TryBrush("BgCardBrush")      ?? Brushes.Black;
        var fg  = TryBrush("TextPrimaryBrush") ?? Brushes.White;
        var dim = TryBrush("TextDimBrush")     ?? Brushes.Gray;

        var win = new Window
        {
            Title = "ショートカット追加", Width = 340, Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = (Brush)bg
        };
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(new TextBlock { Text = "表示名", FontSize = 12, Foreground = (Brush)dim, Margin = new Thickness(0, 0, 0, 4) });
        var txtName = new TextBox { Text = defaultName, Style = (Style)FindResource("DarkTextBox"), Margin = new Thickness(0, 0, 0, 10) };
        sp.Children.Add(txtName);
        sp.Children.Add(new TextBlock { Text = "アイコン（絵文字）", FontSize = 12, Foreground = (Brush)dim, Margin = new Thickness(0, 0, 0, 4) });
        var txtIcon = new TextBox { Text = "🔗", Style = (Style)FindResource("DarkTextBox"), Margin = new Thickness(0, 0, 0, 14) };
        sp.Children.Add(txtIcon);
        var btnOk = new Button
        {
            Content = "追加", Style = (Style)FindResource("PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(20, 6, 20, 6)
        };
        btnOk.Click += (_, _) =>
        {
            sc.Name = string.IsNullOrWhiteSpace(txtName.Text) ? defaultName : txtName.Text.Trim();
            sc.Icon = string.IsNullOrWhiteSpace(txtIcon.Text) ? "🔗" : txtIcon.Text.Trim();
            win.DialogResult = true;
        };
        sp.Children.Add(btnOk);
        win.Content = sp;
        win.Loaded += (_, _) => txtName.Focus();

        if (win.ShowDialog() == true)
        {
            _shortcuts.Add(sc);
            RefreshShortcutList();
        }
    }

    // ══════ 保存 ══════

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtGraceDays.Text, out var days) || days < 0)
        { AppDialog.ShowInfo("猶予日数は0以上の整数を入力してください", "入力エラー", Window.GetWindow(this)); return; }

        var bgPath = string.IsNullOrWhiteSpace(TxtBgPath.Text) ? null : TxtBgPath.Text;
        if (bgPath != null && !File.Exists(bgPath))
        { AppDialog.ShowInfo("背景ファイルが見つかりません", "入力エラー", Window.GetWindow(this)); return; }

        _svc.UpdateAlertSettings(days, ChkHideCompleted.IsChecked != true);
        _svc.UpdateAppearanceSettings(
            bgPath,
            RbPersonalMode.IsChecked == true ? "Personal" : "Multi",
            _svc.TaskRowOpacity, _svc.GanttRowColor, _svc.GanttRowOpacity, _svc.RowBorderColor);

        _svc.SaveShortcuts(_shortcuts);

        // ── ログローテーション設定を保存・即時反映 ──
        if (!int.TryParse(TxtLogMaxSizeMB.Text, out var logMb) || logMb <= 0) logMb = 10;
        if (!int.TryParse(TxtLogMaxFiles.Text,  out var logMf) || logMf <= 0) logMf = 7;
        var logRotation = new LogRotationSettings
        {
            Enabled       = ChkLogEnabled.IsChecked == true,
            Period        = (CbLogPeriod.SelectedItem   as ComboBoxItem)?.Tag as string ?? "daily",
            MaxFileSizeMB = logMb,
            MaxFiles      = logMf,
            OnRotate      = (CbLogOnRotate.SelectedItem as ComboBoxItem)?.Tag as string ?? "backup",
        };
        _svc.SaveLogRotation(logRotation);
        AppLogger.Instance.Configure(logRotation);

        if (Window.GetWindow(this) is Views.MainWindow mw)
            mw.ApplyBackground();

        _vm.RefreshAlerts();
        _vm.NavigateToCommand.Execute("Home");
    }

    private void Back_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("Home");

    private static string? NullIfEmpty(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

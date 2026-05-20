using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Models;
using TKer.Services;

namespace TKer.Views.Dialogs;

public partial class AppSettingsDialog : Window
{
    private readonly AppSettingsService _svc;
    private List<AppShortcut> _shortcuts;
    private List<string>      _menuOrder;

    public AppSettingsDialog(AppSettingsService svc)
    {
        _svc = svc;
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));

        // ── 一般設定 ──
        TxtGraceDays.Text          = svc.AlertNotStartedGraceDays.ToString();
        ChkHideCompleted.IsChecked = svc.ShowCompletedInSchedule == false;
        TxtBgPath.Text             = svc.BackgroundImagePath ?? "";

        if (svc.AppMode == "Personal")
            RbPersonalMode.IsChecked = true;
        else
            RbMultiMode.IsChecked = true;

        // ── テーマ ──
        var t = svc.Theme;
        TxtColorTextPrimary.Text = t.TextPrimary ?? "";
        TxtColorTextSecond.Text  = t.TextSecond  ?? "";
        TxtColorAccent.Text      = t.AccentCyan  ?? "";
        TxtColorBgSecond.Text    = t.BgSecondary ?? "";
        TxtColorBgCard.Text      = t.BgCard      ?? "";
        TxtColorBorder.Text      = t.Border      ?? "";

        var opacity = t.BgCardOpacity ?? 1.0;
        SliderOpacity.Value = Math.Clamp(opacity * 100, 0, 100);
        LblOpacity.Text = $"{(int)SliderOpacity.Value}%";

        // フォント選択
        var savedFont = t.FontFamily ?? "";
        foreach (ComboBoxItem item in CbFontFamily.Items)
        {
            if ((item.Tag as string ?? "") == savedFont)
            { CbFontFamily.SelectedItem = item; break; }
        }
        if (CbFontFamily.SelectedItem == null)
            CbFontFamily.SelectedIndex = 0;

        // ── ログローテーション ──
        var lr = svc.LogRotation;
        ChkLogEnabled.IsChecked = lr.Enabled;
        TxtLogMaxSizeMB.Text    = lr.MaxFileSizeMB.ToString();
        TxtLogMaxFiles.Text     = lr.MaxFiles.ToString();
        SelectComboByTag(CbLogPeriod,    lr.Period);
        SelectComboByTag(CbLogOnRotate,  lr.OnRotate);

        // ── ショートカット ──
        _shortcuts = svc.Shortcuts.Select(s => new AppShortcut
        {
            Id = s.Id, Name = s.Name, Icon = s.Icon, Path = s.Path
        }).ToList();
        RefreshShortcutList();

        // ── ツールバー ──
        _menuOrder = svc.MenuOrder.ToList();
        RefreshMenuOrderList();
    }

    // ══════ 一般設定 ══════
    private void BrowseBg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "背景ファイルを選択",
            Filter = "画像・動画ファイル (*.png;*.jpg;*.jpeg;*.bmp;*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm)|*.png;*.jpg;*.jpeg;*.bmp;*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm|画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|動画ファイル (*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm)|*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm"
        };
        if (dlg.ShowDialog() == true)
            TxtBgPath.Text = dlg.FileName;
    }

    private void ClearBg_Click(object sender, RoutedEventArgs e) => TxtBgPath.Text = "";

    // ══════ テーマ ══════
    private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LblOpacity != null)
            LblOpacity.Text = $"{(int)e.NewValue}%";
    }

    // ══════ ショートカット ══════
    private void RefreshShortcutList()
    {
        ScListPanel.Children.Clear();
        var dim   = (Brush)FindResource("TextDimBrush");
        var prim  = (Brush)FindResource("TextPrimaryBrush");
        var bdr   = (Brush)FindResource("BorderBrush");

        if (_shortcuts.Count == 0)
        {
            ScListPanel.Children.Add(new TextBlock
            {
                Text = "ショートカットがありません。「➕ 追加」から登録できます",
                FontSize = 12, Foreground = dim,
                Margin = new Thickness(4, 8, 4, 8)
            });
            return;
        }

        foreach (var sc in _shortcuts.ToList())
        {
            var row = new Border
            {
                BorderBrush = bdr, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 6, 4, 6)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            var iconTb = new TextBlock { Text = sc.Icon, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(iconTb, 0); g.Children.Add(iconTb);

            var nameTb = new TextBlock { Text = sc.Name, FontSize = 13, Foreground = prim, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameTb, 1); g.Children.Add(nameTb);

            var pathTb = new TextBlock { Text = sc.Path, FontSize = 11, Foreground = dim, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(pathTb, 2); g.Children.Add(pathTb);

            var delBtn = new Button
            {
                Content = "🗑️", Padding = new Thickness(6, 2, 6, 2),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
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

        var name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        var sc = new AppShortcut { Name = name, Path = dlg.FileName, Icon = "🔗" };

        // 簡易名前・アイコン入力ダイアログ
        var bg  = (Brush)FindResource("BgCardBrush");
        var fg  = (Brush)FindResource("TextPrimaryBrush");
        var dim = (Brush)FindResource("TextDimBrush");

        var win = new Window
        {
            Title = "ショートカット追加", Width = 320, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize, Background = bg
        };
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(new TextBlock { Text = "表示名", FontSize = 12, Foreground = dim, Margin = new Thickness(0, 0, 0, 4) });
        var txtName = new TextBox { Text = name, Style = (Style)FindResource("DarkTextBox"), Margin = new Thickness(0, 0, 0, 10) };
        sp.Children.Add(txtName);
        sp.Children.Add(new TextBlock { Text = "アイコン（絵文字）", FontSize = 12, Foreground = dim, Margin = new Thickness(0, 0, 0, 4) });
        var txtIcon = new TextBox { Text = "🔗", Style = (Style)FindResource("DarkTextBox"), Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(txtIcon);
        var btnOk = new Button { Content = "追加", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 5, 16, 5) };
        btnOk.Click += (_, _) =>
        {
            sc.Name = string.IsNullOrWhiteSpace(txtName.Text) ? name : txtName.Text.Trim();
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

    // ══════ ツールバー ══════
    private void RefreshMenuOrderList()
    {
        TbListBox.Items.Clear();
        foreach (var name in _menuOrder)
            TbListBox.Items.Add(name);
        if (TbListBox.Items.Count > 0)
            TbListBox.SelectedIndex = 0;
    }

    private void TbMoveUp_Click(object sender, RoutedEventArgs e)   => TbMove(-1);
    private void TbMoveDown_Click(object sender, RoutedEventArgs e) => TbMove(+1);

    private void TbMove(int delta)
    {
        int idx = TbListBox.SelectedIndex;
        if (idx < 0) return;
        int newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= _menuOrder.Count) return;
        (_menuOrder[idx], _menuOrder[newIdx]) = (_menuOrder[newIdx], _menuOrder[idx]);
        RefreshMenuOrderList();
        TbListBox.SelectedIndex = newIdx;
    }

    // ══════ OK / Cancel ══════
    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtGraceDays.Text, out var days) || days < 0)
        { MessageBox.Show("猶予日数は0以上の整数を入力してください"); return; }

        var bgPath = string.IsNullOrWhiteSpace(TxtBgPath.Text) ? null : TxtBgPath.Text;
        if (bgPath != null && !File.Exists(bgPath))
        { MessageBox.Show("背景ファイルが見つかりません"); return; }

        _svc.UpdateAlertSettings(days, ChkHideCompleted.IsChecked != true);
        _svc.UpdateAppearanceSettings(bgPath, RbPersonalMode.IsChecked == true ? "Personal" : "Multi");

        var font = (CbFontFamily.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        _svc.SaveTheme(new ThemeColors
        {
            TextPrimary   = NullIfEmpty(TxtColorTextPrimary.Text),
            TextSecond    = NullIfEmpty(TxtColorTextSecond.Text),
            AccentCyan    = NullIfEmpty(TxtColorAccent.Text),
            BgSecondary   = NullIfEmpty(TxtColorBgSecond.Text),
            BgCard        = NullIfEmpty(TxtColorBgCard.Text),
            Border        = NullIfEmpty(TxtColorBorder.Text),
            BgCardOpacity = SliderOpacity.Value / 100.0,
            FontFamily    = NullIfEmpty(font),
        });

        _svc.SaveShortcuts(_shortcuts);
        _svc.SaveMenuOrder(_menuOrder);

        // ── ログローテーション ──
        if (!int.TryParse(TxtLogMaxSizeMB.Text, out var maxSize) || maxSize < 1) maxSize = 10;
        if (!int.TryParse(TxtLogMaxFiles.Text,  out var maxFiles) || maxFiles < 1) maxFiles = 7;
        var newLr = new TKer.Models.LogRotationSettings
        {
            Enabled       = ChkLogEnabled.IsChecked == true,
            Period        = (CbLogPeriod.SelectedItem    as ComboBoxItem)?.Tag as string ?? "daily",
            MaxFileSizeMB = maxSize,
            MaxFiles      = maxFiles,
            OnRotate      = (CbLogOnRotate.SelectedItem  as ComboBoxItem)?.Tag as string ?? "backup"
        };
        _svc.SaveLogRotation(newLr);
        TKer.Services.AppLogger.Instance.Configure(newLr);

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void SelectComboByTag(ComboBox cb, string tag)
    {
        foreach (ComboBoxItem item in cb.Items)
        {
            if (item.Tag as string == tag)
            { cb.SelectedItem = item; return; }
        }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }
}

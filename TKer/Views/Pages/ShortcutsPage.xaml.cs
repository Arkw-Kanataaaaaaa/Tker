using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;

namespace TKer.Views.Pages;

/// <summary>アプリ・フォルダー・URLへのショートカット一覧を管理するページ。</summary>
public partial class ShortcutsPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;

    /// <summary>ショートカットページを初期化してViewModelを設定する。</summary>
    public ShortcutsPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
    }

    /// <summary>テーマを適用してショートカット一覧を再構築する。</summary>
    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("SC_Header"));
        BuildList();
    }

    /// <summary>保存されているショートカットを読み込んで行ウィジェット一覧を生成する。</summary>
    private void BuildList()
    {
        ShortcutList.Children.Clear();
        var shortcuts = _vm.AppSettingsService.Shortcuts.ToList();

        NoItemBanner.Visibility = shortcuts.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        foreach (var sc in shortcuts)
        {
            ShortcutList.Children.Add(BuildRow(sc, shortcuts));
        }
    }

    /// <summary>ショートカット1件分の行UIを構築して返す。</summary>
    private Border BuildRow(AppShortcut sc, List<AppShortcut> all)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

        // アイコン
        var iconTb = new TextBlock
        {
            Text = sc.Icon, FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(iconTb, 0);
        grid.Children.Add(iconTb);

        // 名前
        var nameTb = new TextBlock
        {
            Text = sc.Name, FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(207, 207, 207)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(nameTb, 1);
        grid.Children.Add(nameTb);

        // パス
        var pathTb = new TextBlock
        {
            Text = sc.Path, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 119, 116)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(pathTb, 2);
        grid.Children.Add(pathTb);

        // ボタン群
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var openBtn = MakeBtn("▶ 開く", false, () => OpenShortcut(sc.Path));
        var editBtn = MakeBtn("✏️ 編集", false, () => { EditShortcut(sc); BuildList(); });
        var delBtn  = MakeBtn("🗑️", true, () => { DeleteShortcut(sc); BuildList(); });
        btnPanel.Children.Add(openBtn);
        btnPanel.Children.Add(editBtn);
        btnPanel.Children.Add(delBtn);
        Grid.SetColumn(btnPanel, 3);
        grid.Children.Add(btnPanel);

        return new Border
        {
            Child = grid, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(Color.FromRgb(47, 47, 47)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            BorderThickness = new Thickness(1)
        };
    }

    /// <summary>操作ボタンを生成してクリック時にアクションを実行するよう設定する。</summary>
    private static Button MakeBtn(string content, bool danger, Action action)
    {
        var btn = new Button
        {
            Content = content, Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 0, 0), FontSize = 11,
            Foreground = danger
                ? new SolidColorBrush(Color.FromRgb(224, 62, 62))
                : new SolidColorBrush(Color.FromRgb(120, 119, 116)),
            Background = new SolidColorBrush(Color.FromRgb(37, 37, 37)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            BorderThickness = new Thickness(1)
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    /// <summary>新規ショートカット追加ダイアログを開いて保存する。</summary>
    private void AddShortcut_Click(object sender, RoutedEventArgs e)
    {
        var dlg = ShowShortcutDialog(null);
        if (dlg == null) return;
        var list = _vm.AppSettingsService.Shortcuts.ToList();
        list.Add(dlg);
        _vm.AppSettingsService.SaveShortcuts(list);
        BuildList();
    }

    /// <summary>既存ショートカットの編集ダイアログを開いて内容を更新する。</summary>
    private void EditShortcut(AppShortcut sc)
    {
        var updated = ShowShortcutDialog(sc);
        if (updated == null) return;
        var list = _vm.AppSettingsService.Shortcuts.ToList();
        var idx = list.FindIndex(x => x.Id == sc.Id);
        if (idx >= 0) list[idx] = updated;
        _vm.AppSettingsService.SaveShortcuts(list);
    }

    /// <summary>確認ダイアログを表示してショートカットを削除する。</summary>
    private void DeleteShortcut(AppShortcut sc)
    {
        var res = MessageBox.Show($"「{sc.Name}」を削除しますか？",
            "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        var list = _vm.AppSettingsService.Shortcuts.ToList();
        list.RemoveAll(x => x.Id == sc.Id);
        _vm.AppSettingsService.SaveShortcuts(list);
    }

    /// <summary>ショートカットの追加・編集ダイアログを表示して入力結果を返す。</summary>
    private AppShortcut? ShowShortcutDialog(AppShortcut? existing)
    {
        var bg  = new SolidColorBrush(Color.FromRgb(47, 47, 47));
        var fg  = new SolidColorBrush(Color.FromRgb(207, 207, 207));
        var dim = new SolidColorBrush(Color.FromRgb(55, 55, 55));
        var lbl = new SolidColorBrush(Color.FromRgb(120, 119, 116));

        var win = new Window
        {
            Title = existing == null ? "ショートカット追加" : "ショートカット編集",
            Width = 460, Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32))
        };

        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        TextBox MakeField(string label, string val)
        {
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = lbl, Margin = new Thickness(0, 0, 0, 4) });
            var tb = new TextBox
            {
                Text = val, Background = dim, Foreground = fg,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                BorderThickness = new Thickness(1), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 14)
            };
            sp.Children.Add(tb);
            return tb;
        }

        var txtIcon = MakeField("アイコン（絵文字）", existing?.Icon ?? "🔗");
        var txtName = MakeField("名前", existing?.Name ?? "");

        // パス行（TextBox + Browse ボタン）
        sp.Children.Add(new TextBlock { Text = "パス / URL", FontSize = 12, Foreground = lbl, Margin = new Thickness(0, 0, 0, 4) });
        var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition());
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        var txtPath = new TextBox
        {
            Text = existing?.Path ?? "", Background = dim, Foreground = fg,
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            BorderThickness = new Thickness(1), Padding = new Thickness(6)
        };
        Grid.SetColumn(txtPath, 0);
        var browseBtn = new Button
        {
            Content = "📁 参照", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4),
            Background = dim, Foreground = fg,
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)), BorderThickness = new Thickness(1)
        };
        Grid.SetColumn(browseBtn, 1);
        browseBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "アプリ・ファイルを選択",
                Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true) txtPath.Text = dlg.FileName;
        };
        pathRow.Children.Add(txtPath);
        pathRow.Children.Add(browseBtn);
        sp.Children.Add(pathRow);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "キャンセル", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "保存", Padding = new Thickness(16, 5, 16, 5) };
        btnRow.Children.Add(cancel);
        btnRow.Children.Add(ok);
        sp.Children.Add(btnRow);
        win.Content = sp;

        AppShortcut? result = null;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txtName.Text) || string.IsNullOrWhiteSpace(txtPath.Text))
            { MessageBox.Show("名前とパスは必須です"); return; }
            result = new AppShortcut
            {
                Id   = existing?.Id ?? Guid.NewGuid().ToString("N")[..8],
                Name = txtName.Text.Trim(),
                Icon = string.IsNullOrWhiteSpace(txtIcon.Text) ? "🔗" : txtIcon.Text.Trim(),
                Path = txtPath.Text.Trim()
            };
            win.DialogResult = true;
        };
        cancel.Click += (_, _) => win.DialogResult = false;
        win.Loaded += (_, _) => txtName.Focus();
        win.ShowDialog();
        return result;
    }

    /// <summary>指定パスのアプリ・ファイル・URLをシェルで開く。</summary>
    private static void OpenShortcut(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show($"開けませんでした:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

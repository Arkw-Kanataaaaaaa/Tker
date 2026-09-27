using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;

namespace TKer.Views.Pages;

/// <summary>コレクション内のアイテムを管理するページ。</summary>
public partial class CollectionItemsPage : Page
{
    private readonly MainViewModel _vm;
    private readonly Collection    _col;
    private string? _selectedItemId;
    private string? _editingItemId;
    private readonly Dictionary<string, string> _editingValues = new();

    public CollectionItemsPage(MainViewModel vm)
    {
        _vm  = vm;
        _col = vm.SelectedCollection!;
        InitializeComponent();
        TxtHeaderIcon.Text = _col.Icon;
        TxtHeaderName.Text = _col.Name;
        BuildColumnHeader();
        RefreshList();
    }

    // ── ヘッダー ────────────────────────────────────────────

    private void Back_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("Collection");

    // ── 列ヘッダー（動的生成） ──────────────────────────────

    private void BuildColumnHeader()
    {
        var g = MakeRowGrid();
        AddHeaderCell(g, "名前", 0);
        for (int i = 0; i < _col.Fields.Count; i++)
            AddHeaderCell(g, _col.Fields[i].Name, i + 1);
        AddHeaderCell(g, "追加日", _col.Fields.Count + 1);

        ItemsListHeader.Child = new Border
        {
            Background      = R<Brush>("BgSecondaryBrush"),
            BorderBrush     = R<Brush>("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(16, 7, 16, 7),
            Child           = g,
        };
    }

    // ── アイテム一覧 ────────────────────────────────────────

    private void RefreshList()
    {
        TxtItemCount.Text = $"{_col.Items.Count} 件";
        EmptyStatePanel.Visibility = _col.Items.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        ItemsListPanel.Children.Clear();
        foreach (var item in _col.Items.OrderBy(x => x.AddedAt))
            ItemsListPanel.Children.Add(BuildItemRow(item));

        UpdateToolbarState();
    }

    private UIElement BuildItemRow(CollectionItem item)
    {
        bool isSel  = item.Id == _selectedItemId;
        var selBg   = new SolidColorBrush(Color.FromArgb(50, 35, 131, 226));
        var hoverBg = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

        var g = MakeRowGrid();
        AddCell(g, item.Name, 0, isSel);
        for (int i = 0; i < _col.Fields.Count; i++)
        {
            var f       = _col.Fields[i];
            var val     = item.FieldValues.TryGetValue(f.Id, out var v) ? v : "";
            var display = f.FieldType == "ファイル" && !string.IsNullOrEmpty(val)
                ? $"📁 {Path.GetFileName(val)}" : val;
            AddCell(g, display, i + 1, isSel);
        }
        AddCell(g, item.AddedAt.ToString("yyyy/MM/dd"), _col.Fields.Count + 1, isSel);

        var row = new Border
        {
            Background      = isSel ? selBg : Brushes.Transparent,
            BorderBrush     = R<Brush>("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(16, 9, 16, 9),
            Cursor          = Cursors.Hand,
            Child           = g,
        };
        row.MouseEnter        += (_, _) => { if (item.Id != _selectedItemId) row.Background = hoverBg; };
        row.MouseLeave        += (_, _) => { if (item.Id != _selectedItemId) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) =>
        {
            _selectedItemId = _selectedItemId == item.Id ? null : item.Id;
            RefreshList();
        };
        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            _selectedItemId = item.Id;
            ShowForm(item);
        };
        return row;
    }

    // ── グリッドヘルパー ────────────────────────────────────

    private Grid MakeRowGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var _ in _col.Fields)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        return g;
    }

    private void AddHeaderCell(Grid g, string text, int col)
    {
        var tb = new TextBlock
        {
            Text              = text,
            FontSize          = 12,
            FontWeight        = FontWeights.SemiBold,
            Foreground        = R<Brush>("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    private void AddCell(Grid g, string text, int col, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text              = text,
            FontSize          = 13,
            Foreground        = R<Brush>("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            FontWeight        = bold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    // ── ツールバー ──────────────────────────────────────────

    private void UpdateToolbarState()
    {
        bool hasSel = _selectedItemId != null;
        BtnEdit.IsEnabled   = hasSel;
        BtnDelete.IsEnabled = hasSel;
        BtnEdit.Opacity     = hasSel ? 1.0 : 0.35;
        BtnDelete.Opacity   = hasSel ? 1.0 : 0.35;
    }

    private void AddItem_Click(object sender, RoutedEventArgs e) => ShowForm(null);

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItemId == null) return;
        var item = _col.Items.FirstOrDefault(x => x.Id == _selectedItemId);
        if (item != null) ShowForm(item);
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItemId == null) return;
        var item = _col.Items.FirstOrDefault(x => x.Id == _selectedItemId);
        if (item == null) return;
        if (MessageBox.Show($"「{item.Name}」を削除しますか？",
                "削除確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _col.Items.Remove(item);
        _col.UpdatedAt = DateTime.Now;
        _vm.CollectionService.Update(_col);
        _selectedItemId = null;
        RefreshList();
    }

    private void OrganizeFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_col.FolderPath))
        {
            MessageBox.Show(
                "このコレクションにはフォルダパスが設定されていません。\n" +
                "コレクション一覧の編集からフォルダパスを設定してください。",
                "フォルダ整理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!Directory.Exists(_col.FolderPath))
        {
            MessageBox.Show("設定されたフォルダが見つかりません。", "フォルダ整理",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ShellHelper.OpenInExplorer(_col.FolderPath);
    }

    // ── フォームドロワー ────────────────────────────────────

    private void ShowForm(CollectionItem? existing)
    {
        _editingItemId = existing?.Id;
        _editingValues.Clear();

        TxtFormTitle.Text   = existing == null ? "アイテムを追加" : "アイテムを編集";
        BtnFormSave.Content = existing == null ? "追加" : "保存";

        if (existing != null)
        {
            _editingValues["__name__"] = existing.Name;
            foreach (var kv in existing.FieldValues)
                _editingValues[kv.Key] = kv.Value;
        }

        BuildFormContent();
        OpenFormDrawer();
    }

    private void BuildFormContent()
    {
        FormContentPanel.Children.Clear();

        // ── 名前 / ファイルパス ──
        FormContentPanel.Children.Add(MakeFormLabel(
            _col.ItemFormat == "ファイル" ? "ファイルパス *" : "名前 *"));

        if (_col.ItemFormat == "ファイル")
        {
            var pg = new Grid();
            pg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tb = MakeFormTextBox("__name__");
            Grid.SetColumn(tb, 0); pg.Children.Add(tb);

            var browse = new Button
            {
                Content = "参照...",
                Style   = R<Style>("SecondaryButton"),
                Padding = new Thickness(10, 5, 10, 5),
                Margin  = new Thickness(6, 0, 0, 0),
            };
            browse.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "ファイルを選択" };
                if (dlg.ShowDialog() == true) { _editingValues["__name__"] = dlg.FileName; BuildFormContent(); }
            };
            Grid.SetColumn(browse, 1); pg.Children.Add(browse);
            FormContentPanel.Children.Add(new Border { Margin = new Thickness(0, 0, 0, 14), Child = pg });
        }
        else
        {
            FormContentPanel.Children.Add(MakeFormTextBox("__name__", new Thickness(0, 0, 0, 14)));
        }

        // ── 動的フィールド ──
        foreach (var field in _col.Fields.OrderBy(f => f.Order))
        {
            FormContentPanel.Children.Add(MakeFormLabel(field.Name));

            if (field.FieldType == "ファイル" || field.FieldType == "画像")
            {
                var capField = field;
                var hasVal   = _editingValues.TryGetValue(field.Id, out var filePath) && !string.IsNullOrEmpty(filePath);

                var pg = new Grid { Margin = new Thickness(0, 0, 0, 14) };
                pg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var tb = new System.Windows.Controls.TextBox
                {
                    Text            = hasVal ? filePath : "",
                    IsReadOnly      = true,
                    Style           = R<Style>("DarkTextBox"),
                };
                _editingValues.TryAdd(field.Id, "");
                Grid.SetColumn(tb, 0); pg.Children.Add(tb);

                var browseBtn = new Button
                {
                    Content = "参照...",
                    Style   = R<Style>("SecondaryButton"),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin  = new Thickness(6, 0, 0, 0),
                };
                browseBtn.Click += (_, _) =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Title = "ファイルを選択" };
                    if (dlg.ShowDialog() != true) return;
                    _editingValues[capField.Id] = dlg.FileName;
                    BuildFormContent();
                };
                Grid.SetColumn(browseBtn, 1); pg.Children.Add(browseBtn);
                FormContentPanel.Children.Add(pg);
            }
            else
            {
                // 文字列 / リンク
                FormContentPanel.Children.Add(MakeFormTextBox(field.Id, new Thickness(0, 0, 0, 14)));
            }
        }
    }

    private TextBlock MakeFormLabel(string text) => new()
    {
        Text       = text,
        FontSize   = 12,
        Foreground = R<Brush>("TextDimBrush"),
        Margin     = new Thickness(0, 0, 0, 4),
    };

    private TextBox MakeFormTextBox(string key, Thickness? margin = null)
    {
        _editingValues.TryGetValue(key, out var val);
        var tb = new TextBox
        {
            Text   = val ?? "",
            Style  = R<Style>("DarkTextBox"),
            Margin = margin ?? new Thickness(0),
        };
        tb.TextChanged += (_, _) => _editingValues[key] = tb.Text;
        return tb;
    }

    private void OpenFormDrawer()
    {
        if (FormOverlayRoot.Visibility == Visibility.Visible) return;
        FormOverlayRoot.Visibility = Visibility.Visible;
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 480, To = 0, Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        };
        FormPanelTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
    }

    private void CloseFormDrawer()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0, To = 480, Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn,
            },
        };
        anim.Completed += (_, _) => FormOverlayRoot.Visibility = Visibility.Collapsed;
        FormPanelTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
    }

    private void FormBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
        _editingItemId = null;
        _editingValues.Clear();
        CloseFormDrawer();
    }

    private void CancelForm_Click(object sender, RoutedEventArgs e)
    {
        _editingItemId = null;
        _editingValues.Clear();
        CloseFormDrawer();
    }

    private void SaveForm_Click(object sender, RoutedEventArgs e)
    {
        _editingValues.TryGetValue("__name__", out var name);
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("名前を入力してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_editingItemId == null)
        {
            var item = new CollectionItem { Name = name.Trim(), AddedAt = DateTime.Now };
            foreach (var field in _col.Fields)
                if (_editingValues.TryGetValue(field.Id, out var v) && !string.IsNullOrEmpty(v))
                    item.FieldValues[field.Id] = v;
            _col.Items.Add(item);
        }
        else
        {
            var item = _col.Items.FirstOrDefault(x => x.Id == _editingItemId);
            if (item == null) return;
            item.Name = name.Trim();
            item.FieldValues.Clear();
            foreach (var field in _col.Fields)
                if (_editingValues.TryGetValue(field.Id, out var v) && !string.IsNullOrEmpty(v))
                    item.FieldValues[field.Id] = v;
        }

        _col.UpdatedAt = DateTime.Now;
        _vm.CollectionService.Update(_col);
        _editingItemId = null;
        _editingValues.Clear();
        CloseFormDrawer();
        RefreshList();
    }

    // ── ユーティリティ ──────────────────────────────────────

    private static T? R<T>(string key) where T : class
    {
        try { return Application.Current.Resources[key] as T; }
        catch { return null; }
    }
}

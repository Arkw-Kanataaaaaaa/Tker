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

/// <summary>コレクション内アイテムの一覧・追加・編集・削除を管理するページ。</summary>
public partial class CollectionItemsPage : Page
{
    private readonly MainViewModel _vm;
    private readonly Collection    _col;
    private string? _selectedItemId;

    // フォーム用
    private string? _editingItemId;
    private readonly Dictionary<string, string> _editingValues = new();

    /// <summary>CollectionItemsPage を初期化してアイテム一覧を表示する。</summary>
    public CollectionItemsPage(MainViewModel vm)
    {
        _vm  = vm;
        _col = vm.SelectedCollection!;
        InitializeComponent();
        TxtHeaderIcon.Text = _col.Icon;
        TxtHeaderName.Text = _col.Name;
        BuildHeader();
        RefreshList();
    }

    // ── ヘッダー ─────────────────────────────────────────────

    private void Back_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("Collection");

    // ── 列ヘッダー（動的） ────────────────────────────────────

    private void BuildHeader()
    {
        var g = MakeRowGrid();
        AddHeaderCell(g, "名前", 0);
        for (int i = 0; i < _col.Fields.Count; i++)
            AddHeaderCell(g, _col.Fields[i].Name, i + 1);
        AddHeaderCell(g, "追加日", _col.Fields.Count + 1);

        ItemsListHeader.Child = new Border
        {
            Background      = GetBrush("BgSecondaryBrush"),
            BorderBrush     = GetBrush("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(16, 7, 16, 7),
            Child           = g,
        };
    }

    // ── アイテム一覧 ──────────────────────────────────────────

    private void RefreshList()
    {
        TxtItemCount.Text = $"{_col.Items.Count} 件";
        EmptyStatePanel.Visibility = _col.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            var f = _col.Fields[i];
            var val = item.FieldValues.TryGetValue(f.Id, out var v) ? v : "";
            // 画像フィールドは "(画像)" と表示
            var displayVal = f.FieldType == "画像" && !string.IsNullOrEmpty(val) ? "🖼 (画像)" : val;
            AddCell(g, displayVal, i + 1, false);
        }
        AddCell(g, item.AddedAt.ToString("yyyy/MM/dd"), _col.Fields.Count + 1, false);

        var row = new Border
        {
            Background      = isSel ? selBg : Brushes.Transparent,
            BorderBrush     = GetBrush("BorderBrush"),
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
            if (e.ClickCount == 2)
            {
                _selectedItemId = item.Id;
                ShowForm(item);
            }
        };
        return row;
    }

    // ── グリッドヘルパー ──────────────────────────────────────

    private Grid MakeRowGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var _ in _col.Fields)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); // 追加日
        return g;
    }

    private void AddHeaderCell(Grid g, string text, int col)
    {
        var tb = new TextBlock
        {
            Text              = text,
            FontSize          = 12,
            FontWeight        = FontWeights.SemiBold,
            Foreground        = GetBrush("TextDimBrush"),
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
            Foreground        = GetBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            FontWeight        = bold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    // ── ツールバー ────────────────────────────────────────────

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
                "このコレクションにはフォルダパスが設定されていません。\nコレクション編集からフォルダパスを設定してください。",
                "フォルダ整理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!Directory.Exists(_col.FolderPath))
        {
            MessageBox.Show("設定されたフォルダが見つかりません。", "フォルダ整理", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ShellHelper.OpenInExplorer(_col.FolderPath);
    }

    // ── フォームドロワー ──────────────────────────────────────

    private void ShowForm(CollectionItem? existing)
    {
        _editingItemId = existing?.Id;
        _editingValues.Clear();

        TxtFormTitle.Text   = existing == null ? "アイテムを追加" : "アイテムを編集";
        BtnFormSave.Content = existing == null ? "追加" : "保存";

        // 既存値をコピー
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

        // 名前フィールド（必須）
        var nameLabel = _col.ItemFormat == "ファイル" ? "ファイルパス *" : "名前 *";
        FormContentPanel.Children.Add(MakeLabel(nameLabel));

        if (_col.ItemFormat == "ファイル")
        {
            var pathGrid = new Grid();
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tb = MakeTextBox("__name__");
            Grid.SetColumn(tb, 0);
            pathGrid.Children.Add(tb);
            var btn = new Button
            {
                Content = "参照...",
                Style   = FindStyleResource("SecondaryButton"),
                Padding = new Thickness(10, 5, 10, 5),
                Margin  = new Thickness(6, 0, 0, 0),
            };
            btn.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "ファイルを選択" };
                if (dlg.ShowDialog() == true)
                {
                    _editingValues["__name__"] = dlg.FileName;
                    BuildFormContent();
                }
            };
            Grid.SetColumn(btn, 1);
            pathGrid.Children.Add(btn);
            FormContentPanel.Children.Add(new Border { Margin = new Thickness(0, 0, 0, 14), Child = pathGrid });
        }
        else
        {
            FormContentPanel.Children.Add(MakeTextBox("__name__", margin: new Thickness(0, 0, 0, 14)));
        }

        // 動的フィールド
        foreach (var field in _col.Fields.OrderBy(f => f.Order))
        {
            FormContentPanel.Children.Add(MakeLabel(field.Name));

            if (field.FieldType == "画像")
            {
                var hasVal = _editingValues.TryGetValue(field.Id, out var imgVal) && !string.IsNullOrEmpty(imgVal);
                var previewBorder = new Border
                {
                    Height          = 100,
                    CornerRadius    = new CornerRadius(6),
                    Background      = GetBrush("BgCardBrush"),
                    BorderBrush     = GetBrush("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    ClipToBounds    = true,
                    Margin          = new Thickness(0, 0, 0, 6),
                };
                if (hasVal)
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(imgVal!);
                        var bmp   = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = new MemoryStream(bytes);
                        bmp.CacheOption  = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        previewBorder.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
                    }
                    catch
                    {
                        previewBorder.Child = new TextBlock
                        {
                            Text                = "画像読込エラー",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment   = VerticalAlignment.Center,
                        };
                    }
                }
                else
                {
                    previewBorder.Child = new TextBlock
                    {
                        Text                = "画像なし",
                        Foreground          = GetBrush("TextDimBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center,
                    };
                }
                FormContentPanel.Children.Add(previewBorder);

                var capField   = field;
                var imgBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
                var selBtn = new Button
                {
                    Content = "画像を選択",
                    Style   = FindStyleResource("SecondaryButton"),
                    Padding = new Thickness(10, 5, 10, 5),
                };
                selBtn.Click += (_, _) =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title  = "画像を選択",
                        Filter = "画像ファイル|*.jpg;*.jpeg;*.png;*.bmp;*.gif|すべてのファイル|*.*",
                    };
                    if (dlg.ShowDialog() != true) return;
                    try
                    {
                        var bytes = File.ReadAllBytes(dlg.FileName);
                        _editingValues[capField.Id] = Convert.ToBase64String(bytes);
                        BuildFormContent();
                    }
                    catch { }
                };
                imgBtnPanel.Children.Add(selBtn);
                if (hasVal)
                {
                    var clrBtn = new Button
                    {
                        Content = "クリア",
                        Style   = FindStyleResource("SecondaryButton"),
                        Padding = new Thickness(10, 5, 10, 5),
                        Margin  = new Thickness(8, 0, 0, 0),
                    };
                    clrBtn.Click += (_, _) => { _editingValues.Remove(capField.Id); BuildFormContent(); };
                    imgBtnPanel.Children.Add(clrBtn);
                }
                FormContentPanel.Children.Add(imgBtnPanel);
            }
            else
            {
                FormContentPanel.Children.Add(MakeTextBox(field.Id, margin: new Thickness(0, 0, 0, 14)));
            }
        }
    }

    private TextBlock MakeLabel(string text) => new()
    {
        Text       = text,
        FontSize   = 12,
        Foreground = GetBrush("TextDimBrush"),
        Margin     = new Thickness(0, 0, 0, 4),
    };

    private TextBox MakeTextBox(string key, Thickness? margin = null)
    {
        _editingValues.TryGetValue(key, out var val);
        var tb = new TextBox
        {
            Text   = val ?? "",
            Style  = FindStyleResource("DarkTextBox"),
            Margin = margin ?? new Thickness(0),
        };
        tb.TextChanged += (_, _) => _editingValues[key] = tb.Text;
        return tb;
    }

    private Style? FindStyleResource(string key)
    {
        try { return (Style)Application.Current.Resources[key]; }
        catch { return null; }
    }

    private void OpenFormDrawer()
    {
        if (FormOverlayRoot.Visibility == Visibility.Visible) return;
        FormOverlayRoot.Visibility = Visibility.Visible;
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From           = 480,
            To             = 0,
            Duration       = TimeSpan.FromMilliseconds(260),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            },
        };
        FormPanelTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
    }

    private void CloseFormDrawer()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From           = 0,
            To             = 480,
            Duration       = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
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
            // 新規追加
            var item = new CollectionItem { Name = name.Trim(), AddedAt = DateTime.Now };
            foreach (var field in _col.Fields)
                if (_editingValues.TryGetValue(field.Id, out var v))
                    item.FieldValues[field.Id] = v;
            _col.Items.Add(item);
        }
        else
        {
            // 既存編集
            var item = _col.Items.FirstOrDefault(x => x.Id == _editingItemId);
            if (item == null) return;
            item.Name = name.Trim();
            item.FieldValues.Clear();
            foreach (var field in _col.Fields)
                if (_editingValues.TryGetValue(field.Id, out var v))
                    item.FieldValues[field.Id] = v;
        }

        _col.UpdatedAt = DateTime.Now;
        _vm.CollectionService.Update(_col);
        _editingItemId = null;
        _editingValues.Clear();
        CloseFormDrawer();
        RefreshList();
    }

    // ── ユーティリティ ────────────────────────────────────────

    private static Brush GetBrush(string key)
    {
        try { return (Brush)Application.Current.Resources[key]; }
        catch { return Brushes.Transparent; }
    }
}

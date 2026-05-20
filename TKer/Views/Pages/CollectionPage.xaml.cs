using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;

namespace TKer.Views.Pages;

/// <summary>コレクションの一覧・詳細・作成・編集を管理するページ。</summary>
public partial class CollectionPage : Page, IRefreshable
{
    private readonly MainViewModel     _vm;
    private readonly CollectionService _svc;
    private string? _selectedId;
    private bool    _searchVisible;

    // ── インラインフォーム用 ──
    private readonly List<CollectionField> _formFields = new();
    private string? _editingId;

    private enum RightPanel { Empty, Form, Detail }

    /// <summary>コレクションページを初期化してデータを表示する。</summary>
    public CollectionPage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = vm.CollectionService;
        InitializeComponent();
        Refresh();
    }

    /// <summary>一覧・詳細・ツールバーを最新状態に更新する。</summary>
    public void Refresh()
    {
        RefreshList();
        if (FormPanel.Visibility != Visibility.Visible)
            ShowDetail(_selectedId);
        UpdateToolbarState();
    }

    // ══════ 右パネル3状態 ══════

    /// <summary>右パネルの表示状態（空・フォーム・詳細）を切り替える。</summary>
    private void SetRightPanel(RightPanel panel)
    {
        EmptyStatePanel.Visibility = panel == RightPanel.Empty  ? Visibility.Visible : Visibility.Collapsed;
        FormPanel.Visibility       = panel == RightPanel.Form   ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility     = panel == RightPanel.Detail ? Visibility.Visible : Visibility.Collapsed;
    }

    // ══════ ツールバー状態 ══════

    /// <summary>選択状態に応じてツールバーボタンの有効・無効を更新する。</summary>
    private void UpdateToolbarState()
    {
        bool hasSel = _selectedId != null;
        BtnToolbarEdit.IsEnabled   = hasSel;
        BtnToolbarDelete.IsEnabled = hasSel;
        BtnToolbarEdit.Opacity     = hasSel ? 1.0 : 0.35;
        BtnToolbarDelete.Opacity   = hasSel ? 1.0 : 0.35;

        BtnToolbarSearch.Background = _searchVisible
            ? new SolidColorBrush(Color.FromArgb(55, 35, 131, 226))
            : Brushes.Transparent;
    }

    // ══════ 左パネル：コレクション一覧 ══════

    /// <summary>検索フィルターを適用してコレクション一覧を再描画する。</summary>
    private void RefreshList()
    {
        var filter = TxtSearch.Text;
        CollectionListPanel.Children.Clear();

        var list = string.IsNullOrWhiteSpace(filter)
            ? _svc.Collections.ToList()
            : _svc.Collections
                .Where(c => (c.Name ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            (c.Description ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (list.Count == 0)
        {
            CollectionListPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(filter)
                    ? "コレクションがありません\n「新規」ボタンから作成できます"
                    : "該当するコレクションがありません",
                FontSize     = 12,
                Foreground   = Brush("TextDimBrush"),
                Margin       = new Thickness(14, 14, 14, 14),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var col in list)
            CollectionListPanel.Children.Add(BuildCollectionRow(col));
    }

    /// <summary>コレクション1件分のリスト行UIを生成して返す。</summary>
    private Border BuildCollectionRow(Collection col)
    {
        bool isSel  = col.Id == _selectedId;
        var selBg   = new SolidColorBrush(Color.FromArgb(50, 35, 131, 226));
        var hoverBg = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconTb = new TextBlock { Text = col.Icon, FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(iconTb, 0); g.Children.Add(iconTb);

        var namePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
        namePanel.Children.Add(new TextBlock
        {
            Text = col.Name, FontSize = 13,
            FontWeight = isSel ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brush("TextPrimaryBrush"),
        });
        if (!string.IsNullOrEmpty(col.Description))
            namePanel.Children.Add(new TextBlock
            {
                Text = col.Description, FontSize = 11,
                Foreground = Brush("TextDimBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            });
        Grid.SetColumn(namePanel, 1); g.Children.Add(namePanel);

        // フィールド数バッジ
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(55, 35, 131, 226)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Child = new TextBlock
        {
            Text = col.Fields.Count.ToString(), FontSize = 11,
            Foreground = Brush("AccentCyanBrush"),
            FontFamily = new FontFamily("Consolas"),
        };
        Grid.SetColumn(badge, 2); g.Children.Add(badge);

        var row = new Border
        {
            Background = isSel ? selBg : Brushes.Transparent,
            Padding = new Thickness(10, 9, 10, 9),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = g,
        };
        row.MouseEnter        += (_, _) => { if (col.Id != _selectedId) row.Background = hoverBg; };
        row.MouseLeave        += (_, _) => { if (col.Id != _selectedId) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) =>
        {
            _selectedId = col.Id;
            if (FormPanel.Visibility != Visibility.Visible)
            {
                RefreshList();
                ShowDetail(col.Id);
            }
            else
            {
                RefreshList();
            }
            UpdateToolbarState();
        };
        return row;
    }

    // ══════ 右パネル：詳細 ══════

    /// <summary>指定IDのコレクション詳細を右パネルに表示する。</summary>
    private void ShowDetail(string? id)
    {
        var col = id == null ? null : _svc.Collections.FirstOrDefault(c => c.Id == id);
        if (col == null)
        {
            SetRightPanel(RightPanel.Empty);
            return;
        }
        SetRightPanel(RightPanel.Detail);

        TxtDetailIcon.Text = col.Icon;
        TxtDetailName.Text = col.Name;
        TxtDetailMeta.Text = BuildMeta(col);

        BuildDetailContent(col);
    }

    /// <summary>コレクションの基本情報・フィールド一覧を詳細パネルに描画する。</summary>
    private void BuildDetailContent(Collection col)
    {
        DetailContentPanel.Children.Clear();

        // ── 基本情報カード ──
        var infoCard = new Border
        {
            Background = Brush("BgCardBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 16),
        };
        var infoSp = new StackPanel();

        void AddInfoRow(string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock
            {
                Text = label, FontSize = 11,
                Foreground = Brush("TextDimBrush"),
                Margin = new Thickness(0, 0, 0, 2),
            });
            row.Children.Add(new TextBlock
            {
                Text = value, FontSize = 13,
                Foreground = Brush("TextPrimaryBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            infoSp.Children.Add(row);
        }

        if (!string.IsNullOrEmpty(col.Description))
            AddInfoRow("説明", col.Description);

        // アイテム形式バッジ
        var fmtRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        fmtRow.Children.Add(new TextBlock
        {
            Text = "アイテム形式", FontSize = 11,
            Foreground = Brush("TextDimBrush"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var fmtBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 35, 131, 226)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
        };
        fmtBadge.Child = new TextBlock
        {
            Text = col.ItemFormat == "ファイル" ? "📁 ファイル指定" : "📝 文字列",
            FontSize = 12,
            Foreground = Brush("AccentCyanBrush"),
        };
        fmtRow.Children.Add(fmtBadge);
        infoSp.Children.Add(fmtRow);

        if (!string.IsNullOrEmpty(col.FolderPath))
            AddInfoRow("フォルダパス", col.FolderPath);

        if (infoSp.Children.Count == 0)
            infoSp.Children.Add(new TextBlock
            {
                Text = "説明・フォルダパスは未設定です",
                FontSize = 12,
                Foreground = Brush("TextDimBrush"),
            });

        infoCard.Child = infoSp;
        DetailContentPanel.Children.Add(infoCard);

        // ── フィールド一覧 ──
        DetailContentPanel.Children.Add(new TextBlock
        {
            Text = "データ付属情報フィールド",
            FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = Brush("AccentCyanBrush"),
            Margin = new Thickness(0, 0, 0, 10),
        });

        if (col.Fields.Count == 0)
        {
            DetailContentPanel.Children.Add(new TextBlock
            {
                Text = "フィールドが定義されていません。「編集」から追加できます。",
                FontSize = 12, Foreground = Brush("TextDimBrush"),
            });
            return;
        }

        foreach (var field in col.Fields.OrderBy(f => f.Order))
        {
            var (icon, label, badgeColor) = field.FieldType switch
            {
                "画像"  => ("🖼", "画像",  Color.FromArgb(180, 120, 60, 200)),
                "リンク" => ("🔗", "リンク", Color.FromArgb(180, 30, 140, 80)),
                _       => ("📝", "文字列", Color.FromArgb(180, 35, 100, 200)),
            };

            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var fi = new TextBlock { Text = icon, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(fi, 0); g.Children.Add(fi);

            var fn = new TextBlock
            {
                Text = field.Name, FontSize = 13,
                Foreground = Brush("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            Grid.SetColumn(fn, 1); g.Children.Add(fn);

            var bd = new Border
            {
                Background = new SolidColorBrush(badgeColor),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            bd.Child = new TextBlock { Text = label, FontSize = 11, Foreground = Brushes.White };
            Grid.SetColumn(bd, 2); g.Children.Add(bd);

            DetailContentPanel.Children.Add(new Border
            {
                Background = Brush("BgCardBrush"),
                BorderBrush = Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Child = g,
            });
        }
    }

    // ══════ ツールバーイベント ══════

    /// <summary>検索バーの表示・非表示を切り替える。</summary>
    private void ToggleSearch_Click(object sender, RoutedEventArgs e)
    {
        _searchVisible = !_searchVisible;
        SearchPanel.Visibility = _searchVisible ? Visibility.Visible : Visibility.Collapsed;
        if (_searchVisible) TxtSearch.Focus();
        else { TxtSearch.Text = ""; RefreshList(); }
        UpdateToolbarState();
    }

    private void NewCollection_Click(object sender, RoutedEventArgs e) => ShowForm(null);

    /// <summary>選択中のコレクションをフォームで編集する。</summary>
    private void EditCollection_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId == null) return;
        var col = _svc.Collections.FirstOrDefault(c => c.Id == _selectedId);
        if (col == null) return;
        ShowForm(col);
    }

    /// <summary>確認後に選択中のコレクションを削除する。</summary>
    private void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId == null) return;
        var col = _svc.Collections.FirstOrDefault(c => c.Id == _selectedId);
        if (col == null) return;
        if (MessageBox.Show($"「{col.Name}」を削除しますか？",
                "削除確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _svc.Delete(_selectedId);
        _selectedId = null;
        RefreshList();
        ShowDetail(null);
        UpdateToolbarState();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    // ══════ インラインフォーム ══════

    /// <summary>新規または既存コレクションの編集フォームを表示する。</summary>
    private void ShowForm(Collection? existing)
    {
        _editingId = existing?.Id;
        _formFields.Clear();

        TxtFormTitle.Text   = existing == null ? "新規コレクション" : "コレクションを編集";
        BtnFormSave.Content = existing == null ? "作成" : "保存";
        TxtFormName.Text   = existing?.Name        ?? "";
        TxtFormDesc.Text   = existing?.Description ?? "";
        TxtFormFolder.Text = existing?.FolderPath  ?? "";

        // アイテム形式ラジオ
        var fmt = existing?.ItemFormat ?? "文字列";
        RbItemText.IsChecked = fmt != "ファイル";
        RbItemFile.IsChecked = fmt == "ファイル";

        if (existing != null)
            _formFields.AddRange(existing.Fields.Select(f => new CollectionField
            {
                Id = f.Id, Name = f.Name, FieldType = f.FieldType, Order = f.Order,
            }));

        TxtNewFieldName.Text = "";
        CbFieldType.SelectedIndex = 0;

        RefreshFormFieldList();
        SetRightPanel(RightPanel.Form);
        TxtFormName.Focus();
    }

    /// <summary>フォーム内のフィールド一覧を再描画する。</summary>
    private void RefreshFormFieldList()
    {
        FormFieldListPanel.Children.Clear();

        if (_formFields.Count == 0)
        {
            FormFieldListPanel.Children.Add(new TextBlock
            {
                Text = "フィールドが未定義です",
                FontSize = 12,
                Foreground = Brush("TextDimBrush"),
                Margin = new Thickness(0, 0, 0, 8),
            });
            return;
        }

        foreach (var field in _formFields)
            FormFieldListPanel.Children.Add(BuildFormFieldRow(field));
    }

    /// <summary>フォームフィールド1件分の行UIを生成して返す。</summary>
    private UIElement BuildFormFieldRow(CollectionField field)
    {
        var (icon, label, badgeColor) = field.FieldType switch
        {
            "画像"  => ("🖼", "画像",  Color.FromArgb(200, 120, 60, 200)),
            "リンク" => ("🔗", "リンク", Color.FromArgb(200, 30, 140, 80)),
            _       => ("📝", "文字列", Color.FromArgb(200, 35, 100, 200)),
        };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        var iconTb = new TextBlock { Text = icon, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(iconTb, 0); g.Children.Add(iconTb);

        var nameTb = new TextBlock
        {
            Text = field.Name, FontSize = 13,
            Foreground = Brush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        Grid.SetColumn(nameTb, 1); g.Children.Add(nameTb);

        var badge = new Border
        {
            Background = new SolidColorBrush(badgeColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Child = new TextBlock { Text = label, FontSize = 11, Foreground = Brushes.White };
        Grid.SetColumn(badge, 2); g.Children.Add(badge);

        var del = new Button
        {
            Content = "✕", FontSize = 12,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush("TextDimBrush"),
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var cap = field;
        del.Click += (_, _) => { _formFields.Remove(cap); RefreshFormFieldList(); };
        Grid.SetColumn(del, 3); g.Children.Add(del);

        return new Border
        {
            Background = Brush("BgSecondaryBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 5),
            Child = g,
        };
    }

    /// <summary>フォームに新しいフィールドを追加する。</summary>
    private void AddFormField_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtNewFieldName.Text.Trim();
        if (string.IsNullOrEmpty(name)) { TxtNewFieldName.Focus(); return; }

        var type = (CbFieldType.SelectedItem as ComboBoxItem)?.Tag as string ?? "文字列";
        _formFields.Add(new CollectionField { Name = name, FieldType = type, Order = _formFields.Count });

        TxtNewFieldName.Text = "";
        TxtNewFieldName.Focus();
        RefreshFormFieldList();
    }

    /// <summary>フォルダー選択ダイアログを開いてフォルダーパスを設定する。</summary>
    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "コレクションフォルダを選択" };
        if (dlg.ShowDialog() == true)
            TxtFormFolder.Text = dlg.FolderName;
    }

    /// <summary>フォームの内容を検証してコレクションを保存する。</summary>
    private void SaveForm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtFormName.Text)) { TxtFormName.Focus(); return; }

        for (int i = 0; i < _formFields.Count; i++)
            _formFields[i].Order = i;

        var itemFormat = RbItemFile.IsChecked == true ? "ファイル" : "文字列";

        if (_editingId == null)
        {
            var col = new Collection
            {
                Name        = TxtFormName.Text.Trim(),
                Icon        = "📁",
                Description = TxtFormDesc.Text.Trim(),
                FolderPath  = TxtFormFolder.Text.Trim(),
                ItemFormat  = itemFormat,
                Fields      = _formFields.ToList(),
            };
            _svc.Add(col);
            _selectedId = col.Id;
        }
        else
        {
            var existing = _svc.Collections.FirstOrDefault(c => c.Id == _editingId);
            if (existing == null) return;
            existing.Name        = TxtFormName.Text.Trim();
            existing.Description = TxtFormDesc.Text.Trim();
            existing.FolderPath  = TxtFormFolder.Text.Trim();
            existing.ItemFormat  = itemFormat;
            existing.Fields      = _formFields.ToList();
            existing.UpdatedAt   = DateTime.Now;
            _svc.Update(existing);
            _selectedId = _editingId;
        }

        _editingId = null;
        RefreshList();
        ShowDetail(_selectedId);
        UpdateToolbarState();
    }

    /// <summary>フォームをキャンセルして詳細パネルに戻る。</summary>
    private void CancelForm_Click(object sender, RoutedEventArgs e)
    {
        _editingId = null;
        _formFields.Clear();
        ShowDetail(_selectedId);
    }

    // ══════ ユーティリティ ══════

    /// <summary>コレクションのメタ情報文字列を生成して返す。</summary>
    private static string BuildMeta(Collection col)
    {
        var parts = new List<string>();
        parts.Add(col.ItemFormat == "ファイル" ? "📁 ファイル指定" : "📝 文字列");
        if (col.Fields.Count > 0) parts.Add($"{col.Fields.Count} フィールド");
        if (!string.IsNullOrEmpty(col.FolderPath)) parts.Add("フォルダあり");
        return string.Join("  ·  ", parts);
    }

    /// <summary>アプリリソースからブラシを取得して返す。</summary>
    private static Brush Brush(string key)
    {
        try { return (Brush)Application.Current.Resources[key]; }
        catch { return System.Windows.Media.Brushes.Transparent; }
    }
}

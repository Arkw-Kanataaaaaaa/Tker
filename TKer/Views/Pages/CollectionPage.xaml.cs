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
using TKer.Services;
using TKer.ViewModels;

namespace TKer.Views.Pages;

/// <summary>コレクションの一覧・詳細・作成・編集を管理するページ。</summary>
public partial class CollectionPage : Page, IRefreshable
{
    private readonly MainViewModel     _vm;
    private readonly CollectionService _svc;
    private string? _selectedId;
    private bool    _isGridMode = true;

    private readonly List<CollectionField>          _formFields       = new();
    private string?                                 _editingId;
    private string                                  _coverImageData   = string.Empty;
    private readonly Dictionary<string, BitmapImage?> _coverBitmapCache = new();

    /// <summary>コレクションページを初期化してデータを表示する。</summary>
    public CollectionPage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = vm.CollectionService;
        InitializeComponent();
        UpdateDisplayModeButtons();
        Refresh();
    }

    /// <summary>一覧・詳細・ツールバーを最新状態に更新する。</summary>
    public void Refresh()
    {
        ApplyFilter();
        UpdateToolbarState();
    }

    // ── 表示モード ──────────────────────────────────────────

    private void ToggleViewMode_Click(object sender, MouseButtonEventArgs e)
    {
        _isGridMode = !_isGridMode;
        UpdateDisplayModeButtons();
        ApplyFilter();
    }

    private void UpdateDisplayModeButtons()
    {
        // 現在モードのアイコンを表示（グリッド中→グリッドアイコン、リスト中→リストアイコン）
        var iconKey = _isGridMode ? "Bi.Grid3x3Gap" : "Bi.ListUl";
        if (TryFindResource(iconKey) is Geometry geo)
            ViewToggleIcon.Data = geo;
        BtnViewToggle.ToolTip = _isGridMode ? "グリッド表示中（クリックでリスト）" : "リスト表示中（クリックでグリッド）";
    }

    // ── フィルタ・描画 ──────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    /// <summary>検索フィルターを適用してグリッド/リストを再描画する。</summary>
    private void ApplyFilter()
    {
        var filter = SearchBox.Text;
        var list = string.IsNullOrWhiteSpace(filter)
            ? _svc.Collections.ToList()
            : _svc.Collections
                .Where(c => (c.Name ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            (c.Description ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        EmptyStatePanel.Visibility     = list.Count == 0            ? Visibility.Visible : Visibility.Collapsed;
        GridScrollViewer.Visibility    = list.Count > 0 && _isGridMode  ? Visibility.Visible : Visibility.Collapsed;
        CollectionListSection.Visibility = list.Count > 0 && !_isGridMode ? Visibility.Visible : Visibility.Collapsed;

        if (_isGridMode)
        {
            CollectionGridControl.Children.Clear();
            foreach (var col in list)
                CollectionGridControl.Children.Add(BuildCollectionCard(col));
        }
        else
        {
            CollectionListPanel.Children.Clear();
            foreach (var col in list)
                CollectionListPanel.Children.Add(BuildCollectionRow(col));
        }
    }

    private UIElement BuildCollectionCard(Collection col)
    {
        bool isSel = col.Id == _selectedId;

        var card = new Border
        {
            Width           = 170,
            Height          = 220,
            Margin          = new Thickness(6),
            CornerRadius    = new CornerRadius(12),
            ClipToBounds    = true,
            Cursor          = Cursors.Hand,
        };
        card.Clip = new RectangleGeometry(new Rect(0, 0, 170, 220), 12, 12);

        var grid = new Grid();

        // 背景（カバー画像なし時）
        grid.Children.Add(new Border
        {
            Background   = Brush("BgCardBrush"),
            CornerRadius = new CornerRadius(12),
        });

        // カバー画像 or プレースホルダーアイコン
        var coverBmp = TryGetCoverBitmap(col);
        if (coverBmp != null)
        {
            grid.Children.Add(new Image { Source = coverBmp, Stretch = Stretch.UniformToFill });
        }
        else
        {
            grid.Children.Add(new TextBlock
            {
                Text                = col.Icon ?? "📁",
                FontSize            = 52,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            });
        }

        // 選択枠
        if (isSel)
            grid.Children.Add(new Border
            {
                CornerRadius    = new CornerRadius(12),
                BorderThickness = new Thickness(3),
                Background      = Brushes.Transparent,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(35, 131, 226)),
            });

        // プロジェクト名オーバーレイ（下部）
        grid.Children.Add(new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background        = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)),
            Padding           = new Thickness(10, 6, 10, 10),
            Child             = new TextBlock
            {
                Text         = col.Name,
                Foreground   = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF)),
                FontWeight   = FontWeights.Bold,
                FontSize     = 13,
                FontFamily   = new FontFamily("Yu Gothic UI"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        });

        var hoverOverlay = new Border
        {
            Background   = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)),
            CornerRadius = new CornerRadius(12),
            Visibility   = Visibility.Collapsed,
        };
        var hoverSp = new StackPanel
        {
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(12),
        };
        if (!string.IsNullOrEmpty(col.Description))
            hoverSp.Children.Add(new TextBlock
            {
                Text             = col.Description,
                FontSize         = 11,
                Foreground       = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                TextWrapping     = TextWrapping.Wrap,
                TextAlignment    = TextAlignment.Center,
                Margin           = new Thickness(0, 0, 0, 8),
            });
        hoverSp.Children.Add(new TextBlock
        {
            Text                = col.ItemFormat == "ファイル" ? "📁 ファイル指定" : "📝 文字列",
            FontSize            = 12,
            Foreground          = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 0, 0, 4),
        });
        if (col.Fields.Count > 0)
            hoverSp.Children.Add(new TextBlock
            {
                Text                = $"{col.Fields.Count} フィールド",
                FontSize            = 11,
                Foreground          = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        hoverOverlay.Child = hoverSp;
        grid.Children.Add(hoverOverlay);

        card.Child = grid;

        card.MouseEnter        += (_, _) => hoverOverlay.Visibility = Visibility.Visible;
        card.MouseLeave        += (_, _) => hoverOverlay.Visibility = Visibility.Collapsed;
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (_selectedId == col.Id && DetailDrawer.Visibility == Visibility.Visible)
            { CloseDetailDrawer(); return; }
            _selectedId = col.Id;
            ApplyFilter();
            OpenDetailDrawer();
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                _vm.SelectedCollection = col;
                _vm.NavigateToCommand.Execute("CollectionItems");
            }
        };

        return card;
    }

    private UIElement BuildCollectionRow(Collection col)
    {
        bool isSel  = col.Id == _selectedId;
        var selBg   = new SolidColorBrush(Color.FromArgb(50, 35, 131, 226));
        var hoverBg = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

        var g = new Grid { Margin = new Thickness(16, 0, 16, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

        var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(new TextBlock { Text = col.Icon ?? "📁", FontSize = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        namePanel.Children.Add(new TextBlock
        {
            Text       = col.Name,
            FontSize   = 13,
            FontWeight = isSel ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(namePanel, 0); g.Children.Add(namePanel);

        var fmtBadge = new Border
        {
            Background        = new SolidColorBrush(Color.FromArgb(55, 35, 131, 226)),
            CornerRadius      = new CornerRadius(4),
            Padding           = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child             = new TextBlock
            {
                Text       = col.ItemFormat == "ファイル" ? "📁 ファイル" : "📝 文字列",
                FontSize   = 11,
                Foreground = Brush("AccentCyanBrush"),
            },
        };
        Grid.SetColumn(fmtBadge, 1); g.Children.Add(fmtBadge);

        var fieldCntTb = new TextBlock
        {
            Text              = col.Fields.Count.ToString(),
            FontSize          = 13,
            Foreground        = Brush("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(fieldCntTb, 2); g.Children.Add(fieldCntTb);

        var updatedTb = new TextBlock
        {
            Text              = col.UpdatedAt.ToString("yyyy/MM/dd"),
            FontSize          = 12,
            Foreground        = Brush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(updatedTb, 3); g.Children.Add(updatedTb);

        var row = new Border
        {
            Background      = isSel ? selBg : Brushes.Transparent,
            Padding         = new Thickness(0, 10, 0, 10),
            Cursor          = Cursors.Hand,
            BorderBrush     = Brush("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child           = g,
        };
        row.MouseEnter        += (_, _) => { if (col.Id != _selectedId) row.Background = hoverBg; };
        row.MouseLeave        += (_, _) => { if (col.Id != _selectedId) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (_selectedId == col.Id && DetailDrawer.Visibility == Visibility.Visible)
            { CloseDetailDrawer(); return; }
            _selectedId = col.Id;
            ApplyFilter();
            OpenDetailDrawer();
        };
        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                _vm.SelectedCollection = col;
                _vm.NavigateToCommand.Execute("CollectionItems");
            }
        };

        return row;
    }

    // ── ツールバー状態 ──────────────────────────────────────

    private void UpdateToolbarState()
    {
        bool hasSel = _selectedId != null;
        BtnToolbarEdit.IsEnabled   = hasSel;
        BtnToolbarDelete.IsEnabled = hasSel;
        BtnToolbarEdit.Opacity     = hasSel ? 1.0 : 0.35;
        BtnToolbarDelete.Opacity   = hasSel ? 1.0 : 0.35;
    }

    // ── 詳細ドロワー ────────────────────────────────────────

    private void OpenDetailDrawer()
    {
        var col = _selectedId == null ? null : _svc.Collections.FirstOrDefault(c => c.Id == _selectedId);
        if (col == null) return;

        TxtDetailName.Text = col.Name;
        BuildDetailContent(col);

        if (DetailDrawer.Visibility == Visibility.Visible) return;
        DetailDrawer.Visibility = Visibility.Visible;
        FormDrawer.Visibility   = Visibility.Collapsed;

        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0, To = 500,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        DrawerContainer.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void CloseDetailDrawer()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = DrawerContainer.ActualWidth, To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
            }
        };
        anim.Completed += (_, _) =>
        {
            DetailDrawer.Visibility = Visibility.Collapsed;
            _selectedId = null;
            ApplyFilter();
            UpdateToolbarState();
        };
        DrawerContainer.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void CloseDetailPanel_Click(object sender, RoutedEventArgs e) => CloseDetailDrawer();

    private void EditFromDetail_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId == null) return;
        var col = _svc.Collections.FirstOrDefault(c => c.Id == _selectedId);
        if (col == null) return;
        DetailDrawer.Visibility = Visibility.Collapsed;
        ShowForm(col);
    }

    private void DeleteFromDetail_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId == null) return;
        var col = _svc.Collections.FirstOrDefault(c => c.Id == _selectedId);
        if (col == null) return;
        if (MessageBox.Show($"「{col.Name}」を削除しますか？",
                "削除確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        DetailDrawer.Visibility = Visibility.Collapsed;
        _svc.Delete(_selectedId);
        _selectedId = null;
        ApplyFilter();
        UpdateToolbarState();
    }

    // ── フォームドロワー ────────────────────────────────────

    private void OpenFormDrawer()
    {
        if (FormDrawer.Visibility == Visibility.Visible) return;

        // 切替前にドロワーが開いているか判定（詳細→フォームではしまわず内容だけ差し替える）
        bool wasOpen = DetailDrawer.Visibility == Visibility.Visible || DrawerContainer.ActualWidth > 0;

        FormDrawer.Visibility   = Visibility.Visible;
        DetailDrawer.Visibility = Visibility.Collapsed;

        double from = wasOpen ? DrawerContainer.ActualWidth : 0;
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = from, To = 500,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        DrawerContainer.BeginAnimation(FrameworkElement.WidthProperty, anim);
        TxtFormName.Focus();
    }

    private void CloseFormDrawer()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = DrawerContainer.ActualWidth, To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
            }
        };
        anim.Completed += (_, _) => FormDrawer.Visibility = Visibility.Collapsed;
        DrawerContainer.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    // ── ツールバーイベント ──────────────────────────────────

    private void ToggleSearch_Click(object sender, RoutedEventArgs e)
    {
        bool willClose = SearchSection.Visibility == Visibility.Visible;
        SearchBarHelper.Toggle(SearchSection, SearchBox);
        if (willClose) { SearchBox.Text = ""; ApplyFilter(); }
    }

    private void NewCollection_Click(object sender, RoutedEventArgs e) => ShowForm(null);

    private void EditCollection_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId == null) return;
        var col = _svc.Collections.FirstOrDefault(c => c.Id == _selectedId);
        if (col == null) return;
        ShowForm(col);
    }

    private void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId == null) return;
        var col = _svc.Collections.FirstOrDefault(c => c.Id == _selectedId);
        if (col == null) return;
        if (MessageBox.Show($"「{col.Name}」を削除しますか？",
                "削除確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (DetailDrawer.Visibility == Visibility.Visible)
            DetailDrawer.Visibility = Visibility.Collapsed;
        _svc.Delete(_selectedId);
        _selectedId = null;
        ApplyFilter();
        UpdateToolbarState();
    }

    // ── インラインフォーム ──────────────────────────────────

    private void ShowForm(Collection? existing)
    {
        // 新規作成時は選択中コレクションを解除する
        if (existing == null)
        {
            _selectedId = null;
            ApplyFilter();
        }

        _editingId = existing?.Id;
        _formFields.Clear();

        TxtFormTitle.Text   = existing == null ? "新規コレクション" : "コレクションを編集";
        BtnFormSave.Content = existing == null ? "作成" : "保存";
        TxtFormName.Text   = existing?.Name        ?? "";
        TxtFormDesc.Text   = existing?.Description ?? "";
        TxtFormFolder.Text = existing?.FolderPath  ?? "";

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

        ClearCoverImageField();
        if (!string.IsNullOrEmpty(existing?.CoverImageData))
        {
            try
            {
                var bytes = Convert.FromBase64String(existing.CoverImageData);
                _coverImageData = existing.CoverImageData;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                CoverImagePreview.Source         = bmp;
                CoverImagePreview.Visibility     = Visibility.Visible;
                CoverImagePlaceholder.Visibility = Visibility.Collapsed;
                BtnClearCoverImage.Visibility    = Visibility.Visible;
            }
            catch { }
        }

        RefreshFormFieldList();
        OpenFormDrawer();
    }

    private void RefreshFormFieldList()
    {
        FormFieldListPanel.Children.Clear();
        if (_formFields.Count == 0)
        {
            FormFieldListPanel.Children.Add(new TextBlock
            {
                Text       = "フィールドが未定義です",
                FontSize   = 12,
                Foreground = Brush("TextDimBrush"),
                Margin     = new Thickness(0, 0, 0, 8),
            });
            return;
        }
        foreach (var field in _formFields)
            FormFieldListPanel.Children.Add(BuildFormFieldRow(field));
    }

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
            Background        = new SolidColorBrush(badgeColor),
            CornerRadius      = new CornerRadius(4),
            Padding           = new Thickness(7, 2, 7, 2),
            Margin            = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child             = new TextBlock { Text = label, FontSize = 11, Foreground = Brushes.White },
        };
        Grid.SetColumn(badge, 2); g.Children.Add(badge);

        var del = new Button
        {
            Content         = "✕",
            FontSize        = 12,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground      = Brush("TextDimBrush"),
            Cursor          = Cursors.Hand,
            Padding         = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var cap = field;
        del.Click += (_, _) => { _formFields.Remove(cap); RefreshFormFieldList(); };
        Grid.SetColumn(del, 3); g.Children.Add(del);

        return new Border
        {
            Background      = Brush("BgSecondaryBrush"),
            BorderBrush     = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10, 7, 10, 7),
            Margin          = new Thickness(0, 0, 0, 5),
            Child           = g,
        };
    }

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

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "コレクションフォルダを選択" };
        if (dlg.ShowDialog() == true)
            TxtFormFolder.Text = dlg.FolderName;
    }

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
                Name          = TxtFormName.Text.Trim(),
                Icon          = "📁",
                Description   = TxtFormDesc.Text.Trim(),
                FolderPath    = TxtFormFolder.Text.Trim(),
                ItemFormat    = itemFormat,
                Fields        = _formFields.ToList(),
                CoverImageData = _coverImageData,
            };
            _svc.Add(col);
            _selectedId = col.Id;
            _coverBitmapCache.Remove(col.Id);
        }
        else
        {
            var existing = _svc.Collections.FirstOrDefault(c => c.Id == _editingId);
            if (existing == null) return;
            existing.Name          = TxtFormName.Text.Trim();
            existing.Description   = TxtFormDesc.Text.Trim();
            existing.FolderPath    = TxtFormFolder.Text.Trim();
            existing.ItemFormat    = itemFormat;
            existing.Fields        = _formFields.ToList();
            existing.CoverImageData = _coverImageData;
            existing.UpdatedAt     = DateTime.Now;
            _svc.Update(existing);
            _selectedId = _editingId;
            _coverBitmapCache.Remove(_editingId);
        }

        _editingId = null;
        CloseFormDrawer();
        ApplyFilter();
        UpdateToolbarState();
    }

    private void CancelForm_Click(object sender, RoutedEventArgs e)
    {
        _editingId = null;
        _formFields.Clear();
        CloseFormDrawer();
    }

    // ── 詳細コンテンツ ──────────────────────────────────────

    private void BuildDetailContent(Collection col)
    {
        DetailContentPanel.Children.Clear();

        var coverBmp = TryGetCoverBitmap(col);
        if (coverBmp != null)
            DetailContentPanel.Children.Add(new Border
            {
                Height       = 160,
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Margin       = new Thickness(0, 0, 0, 16),
                Child        = new Image { Source = coverBmp, Stretch = Stretch.UniformToFill },
            });

        var infoCard = new Border
        {
            Background      = Brush("BgCardBrush"),
            BorderBrush     = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(16, 14, 16, 14),
            Margin          = new Thickness(0, 0, 0, 16),
        };
        var infoSp = new StackPanel();

        void AddInfoRow(string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            infoSp.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8),
                Children =
                {
                    new TextBlock { Text = label, FontSize = 11, Foreground = Brush("TextDimBrush"), Margin = new Thickness(0, 0, 0, 2) },
                    new TextBlock { Text = value, FontSize = 13, Foreground = Brush("TextPrimaryBrush"), TextWrapping = TextWrapping.Wrap },
                },
            });
        }

        if (!string.IsNullOrEmpty(col.Description))
            AddInfoRow("説明", col.Description);

        var fmtRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        fmtRow.Children.Add(new TextBlock
        {
            Text              = "アイテム形式", FontSize = 11,
            Foreground        = Brush("TextDimBrush"),
            Margin            = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        fmtRow.Children.Add(new Border
        {
            Background   = new SolidColorBrush(Color.FromArgb(60, 35, 131, 226)),
            CornerRadius = new CornerRadius(4),
            Padding      = new Thickness(8, 3, 8, 3),
            Child        = new TextBlock
            {
                Text       = col.ItemFormat == "ファイル" ? "📁 ファイル指定" : "📝 文字列",
                FontSize   = 12,
                Foreground = Brush("AccentCyanBrush"),
            },
        });
        infoSp.Children.Add(fmtRow);

        if (!string.IsNullOrEmpty(col.FolderPath))
            AddInfoRow("フォルダパス", col.FolderPath);

        AddInfoRow("作成日", col.CreatedAt.ToString("yyyy/MM/dd"));

        if (infoSp.Children.Count == 0)
            infoSp.Children.Add(new TextBlock
            {
                Text       = "説明・フォルダパスは未設定です",
                FontSize   = 12,
                Foreground = Brush("TextDimBrush"),
            });

        infoCard.Child = infoSp;
        DetailContentPanel.Children.Add(infoCard);

        DetailContentPanel.Children.Add(new TextBlock
        {
            Text       = "データ付属情報フィールド",
            FontSize   = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("AccentCyanBrush"),
            Margin     = new Thickness(0, 0, 0, 10),
        });

        if (col.Fields.Count == 0)
        {
            DetailContentPanel.Children.Add(new TextBlock
            {
                Text       = "フィールドが定義されていません。「編集」から追加できます。",
                FontSize   = 12,
                Foreground = Brush("TextDimBrush"),
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
                Background        = new SolidColorBrush(badgeColor),
                CornerRadius      = new CornerRadius(4),
                Padding           = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child             = new TextBlock { Text = label, FontSize = 11, Foreground = Brushes.White },
            };
            Grid.SetColumn(bd, 2); g.Children.Add(bd);

            DetailContentPanel.Children.Add(new Border
            {
                Background      = Brush("BgCardBrush"),
                BorderBrush     = Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(10, 8, 10, 8),
                Margin          = new Thickness(0, 0, 0, 6),
                Child           = g,
            });
        }
    }

    // ── 表紙画像 ────────────────────────────────────────────

    private void ClearCoverImageField()
    {
        _coverImageData                  = string.Empty;
        CoverImagePreview.Source         = null;
        CoverImagePreview.Visibility     = Visibility.Collapsed;
        CoverImagePlaceholder.Visibility = Visibility.Visible;
        BtnClearCoverImage.Visibility    = Visibility.Collapsed;
    }

    private void BrowseCoverImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "表紙画像を選択",
            Filter = "画像ファイル|*.jpg;*.jpeg;*.png;*.bmp;*.gif|すべてのファイル|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            _coverImageData = Convert.ToBase64String(bytes);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            CoverImagePreview.Source         = bmp;
            CoverImagePreview.Visibility     = Visibility.Visible;
            CoverImagePlaceholder.Visibility = Visibility.Collapsed;
            BtnClearCoverImage.Visibility    = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"画像の読み込みに失敗しました:\n{ex.Message}", "エラー");
        }
    }

    private void ClearCoverImage_Click(object sender, RoutedEventArgs e) => ClearCoverImageField();

    private BitmapImage? TryGetCoverBitmap(Collection col)
    {
        if (_coverBitmapCache.TryGetValue(col.Id, out var cached)) return cached;
        if (string.IsNullOrEmpty(col.CoverImageData))
        { _coverBitmapCache[col.Id] = null; return null; }
        try
        {
            var bytes = Convert.FromBase64String(col.CoverImageData);
            var bmp   = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _coverBitmapCache[col.Id] = bmp;
            return bmp;
        }
        catch { _coverBitmapCache[col.Id] = null; return null; }
    }

    // ── ユーティリティ ──────────────────────────────────────

    private static Brush Brush(string key)
    {
        try { return (Brush)Application.Current.Resources[key]; }
        catch { return Brushes.Transparent; }
    }
}

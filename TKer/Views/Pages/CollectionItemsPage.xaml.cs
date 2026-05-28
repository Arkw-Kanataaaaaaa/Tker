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
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>コレクション内のアイテムを管理するページ。</summary>
public partial class CollectionItemsPage : Page
{
    private readonly MainViewModel _vm;
    private readonly Collection    _col;
    private string? _selectedItemId;
    private string? _selectedOrphanPath;
    private string? _editingItemId;
    private string  _searchFieldId = "";
    private bool    _isGridMode;
    private bool    _organizing;
    private Window? _keyDownWindow;
    private readonly Dictionary<string, string> _editingValues = new();
    private readonly List<string> _orphans = new();

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff", ".tif" };

    // ── 画像プレビューオーバーレイ ──
    private System.Windows.Point _previewDragStart;
    private System.Windows.Point _previewTranslateStart;
    private bool _isPreviewDragging;
    private bool _previewPressedBackground;
    private bool _zoomDragging;
    private const double PreviewMinScale   = 1.0;
    private const double PreviewMaxScale   = 4.0;
    private const double PreviewGaugeWidth = 160.0;
    private const double PreviewThumbSize  = 14.0;

    public CollectionItemsPage(MainViewModel vm)
    {
        _vm  = vm;
        _col = vm.SelectedCollection!;
        InitializeComponent();
        TxtHeaderName.Text = _col.Name;

        _isGridMode = _col.Fields.Any(f => f.FieldType == "ファイル");

        // フォルダ整理ボタンはファイルフィールドがあるコレクションのみ表示
        BtnOrganize.Visibility = HasFileField()
            ? Visibility.Visible : Visibility.Collapsed;

        PopulateSearchFields();
        UpdateDisplayModeButtons();
        BuildColumnHeader();
        RefreshList();

        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } win)
            {
                win.KeyDown -= Window_KeyDown;
                win.KeyDown += Window_KeyDown;
                _keyDownWindow = win;
            }
        };
        Unloaded += (_, _) =>
        {
            if (_keyDownWindow is { } win)
                win.KeyDown -= Window_KeyDown;
            _keyDownWindow = null;
        };
    }

    // ── 表示モード ──────────────────────────────────────────

    private void ToggleViewMode_Click(object sender, MouseButtonEventArgs e)
    {
        _isGridMode = !_isGridMode;
        UpdateDisplayModeButtons();
        RefreshList();
    }

    private void UpdateDisplayModeButtons()
    {
        ViewIconGrid.Visibility = _isGridMode ? Visibility.Visible   : Visibility.Collapsed;
        ViewIconList.Visibility = _isGridMode ? Visibility.Collapsed : Visibility.Visible;
        BtnViewToggle.ToolTip   = _isGridMode ? "リスト表示に切り替え" : "グリッド表示に切り替え";
        ItemsListHeader.Visibility = _isGridMode ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── ヘッダー ────────────────────────────────────────────

    private void CollectionCrumb_Click(object sender, MouseButtonEventArgs e)
        => _vm.NavigateToCommand.Execute("Collection");

    // ── 検索 ────────────────────────────────────────────────

    private void PopulateSearchFields()
    {
        CbSearchField.Items.Clear();
        CbSearchField.Items.Add(new ComboBoxItem { Content = "すべて", Tag = "" });
        foreach (var f in _col.Fields.OrderBy(x => x.Order))
            CbSearchField.Items.Add(new ComboBoxItem { Content = f.Name, Tag = f.Id });
        CbSearchField.SelectedIndex = 0;
    }

    private void ToggleSearch_Click(object sender, RoutedEventArgs e)
        => SearchBarHelper.Toggle(SearchSection, SearchBox);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void SearchField_Changed(object sender, SelectionChangedEventArgs e)
    {
        _searchFieldId = (CbSearchField.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        RefreshList();
    }

    private bool MatchesSearch(CollectionItem item, string query)
    {
        bool Contains(string? s) => (s ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(_searchFieldId))
            return item.FieldValues.Values.Any(Contains);
        return item.FieldValues.TryGetValue(_searchFieldId, out var v) && Contains(v);
    }

    // ── キーボードショートカット ────────────────────────────

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible) return;

        bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt   = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (ctrl && !shift && !alt)
        {
            switch (e.Key)
            {
                case Key.F:
                    ToggleSearch_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
                case Key.OemMinus:
                case Key.Subtract:
                    DeleteItem_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
                case Key.M:
                    if (BtnOrganize.Visibility == Visibility.Visible)
                    {
                        ToggleOrganize_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;
            }
        }
        else if (ctrl && shift && !alt)
        {
            if (e.Key == Key.OemSemicolon)
            {
                AddItem_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.F2)
            {
                EditItem_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && PreviewOverlay.Visibility == Visibility.Visible)
            {
                ClosePreviewOverlay();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && FormDrawer.Visibility == Visibility.Visible)
            {
                CancelForm_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchSection.Visibility == Visibility.Visible)
            {
                ToggleSearch_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && DrawerContainer.ActualWidth > 0)
            {
                CloseDetailDrawer();
                e.Handled = true;
            }
        }
    }

    // ── 列ヘッダー ──────────────────────────────────────────

    private int FieldColCount  => Math.Max(_col.Fields.Count, 1);
    private int CreatedColumn  => FieldColCount;
    private int UpdatedColumn  => FieldColCount + 1;

    private void BuildColumnHeader()
    {
        var g = MakeRowGrid();
        for (int i = 0; i < _col.Fields.Count; i++)
            AddHeaderCell(g, _col.Fields[i].Name, i);
        AddHeaderCell(g, "作成日時", CreatedColumn);
        AddHeaderCell(g, "更新日時", UpdatedColumn);

        ItemsListHeader.Child = new Border
        {
            Background      = R<Brush>("BgSecondaryBrush"),
            BorderBrush     = R<Brush>("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(16, 7, 16, 7),
            Child           = g,
        };
    }

    // ── 一覧描画 ────────────────────────────────────────────

    private void RefreshList()
    {
        var query = SearchBox?.Text?.Trim() ?? "";
        var seq   = _col.Items.OrderBy(x => x.AddedAt).AsEnumerable();
        if (!string.IsNullOrEmpty(query))
            seq = seq.Where(it => MatchesSearch(it, query));
        var list = seq.ToList();

        EmptyStatePanel.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_isGridMode)
        {
            GridScrollViewer.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ListScrollViewer.Visibility = Visibility.Collapsed;
            ItemsGridPanel.Children.Clear();
            foreach (var item in list)
                ItemsGridPanel.Children.Add(BuildItemCard(item));
        }
        else
        {
            ListScrollViewer.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            GridScrollViewer.Visibility = Visibility.Collapsed;
            ItemsListPanel.Children.Clear();
            foreach (var item in list)
                ItemsListPanel.Children.Add(BuildItemRow(item));
        }

        UpdateToolbarState();
    }

    // ── グリッドカード ──────────────────────────────────────

    private UIElement BuildItemCard(CollectionItem item)
    {
        bool isSel = item.Id == _selectedItemId;

        var fileField = _col.Fields.FirstOrDefault(f => f.FieldType == "ファイル");
        string? filePath = fileField != null
            && item.FieldValues.TryGetValue(fileField.Id, out var fp) ? fp : null;

        var firstField = _col.Fields.OrderBy(f => f.Order).FirstOrDefault();
        var title = "";
        if (firstField != null && item.FieldValues.TryGetValue(firstField.Id, out var fv))
            title = firstField.FieldType == "ファイル"
                ? Path.GetFileNameWithoutExtension(fv)
                : fv;

        var card = new Border
        {
            Width        = 170, Height = 220,
            Margin       = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Cursor       = Cursors.Hand,
        };
        card.Clip = new RectangleGeometry(new Rect(0, 0, 170, 220), 12, 12);

        var grid = new Grid();
        grid.Children.Add(new Border
        {
            Background   = R<Brush>("BgCardBrush"),
            CornerRadius = new CornerRadius(12),
        });

        if (filePath != null && IsImageFile(filePath) && File.Exists(filePath))
        {
            var bmp = TryLoadBitmap(filePath);
            if (bmp != null)
                grid.Children.Add(new Image { Source = bmp, Stretch = Stretch.UniformToFill });
            else
                AddFileIconPlaceholder(grid);
        }
        else
        {
            AddFileIconPlaceholder(grid);
        }

        if (isSel)
            grid.Children.Add(new Border
            {
                CornerRadius    = new CornerRadius(12),
                BorderThickness = new Thickness(3),
                Background      = Brushes.Transparent,
                BorderBrush     = R<Brush>("AccentCyanBrush"),
            });

        grid.Children.Add(new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background        = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)),
            Padding           = new Thickness(10, 6, 10, 10),
            Child             = new TextBlock
            {
                Text         = title,
                Foreground   = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF)),
                FontWeight   = FontWeights.Bold,
                FontSize     = 13,
                FontFamily   = new FontFamily("Yu Gothic UI"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        });

        var hoverOverlay = new Border
        {
            CornerRadius     = new CornerRadius(12),
            Background       = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false,
            Visibility       = Visibility.Collapsed,
        };
        grid.Children.Add(hoverOverlay);

        card.Child = grid;

        card.MouseEnter        += (_, _) => hoverOverlay.Visibility = Visibility.Visible;
        card.MouseLeave        += (_, _) => hoverOverlay.Visibility = Visibility.Collapsed;
        card.MouseLeftButtonUp += (_, _) => SelectItem(item);
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            _selectedItemId = item.Id;
            ShowForm(item);
        };

        return card;
    }

    private static void AddFileIconPlaceholder(Grid grid)
    {
        grid.Children.Add(new System.Windows.Shapes.Path
        {
            Data  = Application.Current.Resources["Bi.FileEarmarkText"] as Geometry,
            Style = Application.Current.Resources["BiIconXl"] as Style,
        });
    }

    private static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));

    private static BitmapImage? TryLoadBitmap(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource   = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    // ── リスト行 ────────────────────────────────────────────

    private UIElement BuildItemRow(CollectionItem item)
    {
        bool isSel  = item.Id == _selectedItemId;
        var selBg   = new SolidColorBrush(Color.FromArgb(50, 35, 131, 226));
        var hoverBg = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

        var g = MakeRowGrid();
        for (int i = 0; i < _col.Fields.Count; i++)
        {
            var f   = _col.Fields[i];
            var val = item.FieldValues.TryGetValue(f.Id, out var v) ? v : "";

            if (f.FieldType == "リンク" && !string.IsNullOrEmpty(val))
            {
                var link = BuildLinkText(val, 13);
                link.Margin = ColumnPadding(i);
                Grid.SetColumn(link, i);
                g.Children.Add(link);
            }
            else
            {
                string display;
                if (f.FieldType == "ファイル" && !string.IsNullOrEmpty(val))
                    display = Path.GetFileName(val);
                else if (f.InputFormat == "チェックボックス")
                    display = val == "true" ? "✓" : "";
                else
                    display = val;
                AddCell(g, display, i, isSel && i == 0);
            }
        }
        AddCell(g, item.AddedAt.ToString("yyyy/MM/dd HH:mm"),   CreatedColumn, false);
        AddCell(g, item.UpdatedAt.ToString("yyyy/MM/dd HH:mm"), UpdatedColumn, false);

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
        row.MouseLeftButtonUp += (_, _) => SelectItem(item);
        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            _selectedItemId = item.Id;
            ShowForm(item);
        };
        return row;
    }

    // ── 選択／詳細表示 ──────────────────────────────────────

    private void SelectItem(CollectionItem item)
    {
        if (_selectedItemId == item.Id && DetailDrawer.Visibility == Visibility.Visible)
        {
            CloseDetailDrawer();
            return;
        }
        _selectedItemId = item.Id;
        _selectedOrphanPath = null;
        RefreshList();
        RefreshOrphans();
        OpenDetailDrawer(item);
    }

    // ── フォルダ整理 / 非管理アイテム ─────────────────────────

    private void ToggleOrganize_Click(object sender, RoutedEventArgs e)
    {
        _organizing = !_organizing;
        if (!_organizing)
        {
            _selectedOrphanPath = null;
            OrphansSection.Visibility = Visibility.Collapsed;
            UpdateToolbarState();
            return;
        }
        RefreshOrphans();
    }

    private void RefreshOrphans()
    {
        _orphans.Clear();
        if (!_organizing || !HasFileField())
        {
            OrphansSection.Visibility = Visibility.Collapsed;
            return;
        }
        if (string.IsNullOrEmpty(_col.FolderPath) || !Directory.Exists(_col.FolderPath))
        {
            OrphansSection.Visibility = Visibility.Visible;
            OrphansHeader.Text = "非管理アイテム (0件) — コレクションフォルダがありません";
            OrphansListPanel.Children.Clear();
            return;
        }

        var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _col.Items)
            foreach (var f in _col.Fields.Where(x => x.FieldType == "ファイル"))
                if (item.FieldValues.TryGetValue(f.Id, out var p) && !string.IsNullOrEmpty(p))
                {
                    try { managed.Add(Path.GetFullPath(p)); } catch { }
                }

        try
        {
            foreach (var path in Directory.EnumerateFiles(_col.FolderPath, "*", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(path).EndsWith("_collection.json", StringComparison.OrdinalIgnoreCase)) continue;
                var full = Path.GetFullPath(path);
                if (!managed.Contains(full)) _orphans.Add(path);
            }
        }
        catch { }

        OrphansSection.Visibility = Visibility.Visible;
        OrphansHeader.Text = $"非管理アイテム ({_orphans.Count}件)";
        BuildOrphansList();
    }

    private void BuildOrphansList()
    {
        OrphansListPanel.Children.Clear();
        if (_orphans.Count == 0)
        {
            OrphansListPanel.Children.Add(new TextBlock
            {
                Text       = "未登録のファイルはありません",
                FontSize   = 12,
                Foreground = R<Brush>("TextDimBrush"),
                Margin     = new Thickness(8, 6, 0, 6),
            });
            return;
        }

        foreach (var path in _orphans)
            OrphansListPanel.Children.Add(BuildOrphanRow(path));
    }

    private UIElement BuildOrphanRow(string path)
    {
        bool isSel = string.Equals(path, _selectedOrphanPath, StringComparison.OrdinalIgnoreCase);
        var selBg   = new SolidColorBrush(Color.FromArgb(60, 35, 131, 226));
        var hoverBg = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new System.Windows.Shapes.Path
        {
            Data              = R<Geometry>("Bi.FileEarmarkText"),
            Style             = R<Style>("BiIconSm"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(icon, 0); g.Children.Add(icon);

        var nameTb = new TextBlock
        {
            Text              = Path.GetFileName(path),
            FontSize          = 12,
            Foreground        = R<Brush>("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            ToolTip           = path,
        };
        Grid.SetColumn(nameTb, 1); g.Children.Add(nameTb);

        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }
        var sizeTb = new TextBlock
        {
            Text              = FormatBytes(size),
            FontSize          = 11,
            Foreground        = R<Brush>("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(sizeTb, 2); g.Children.Add(sizeTb);

        var row = new Border
        {
            Background      = isSel ? selBg : Brushes.Transparent,
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10, 6, 10, 6),
            Margin          = new Thickness(0, 0, 0, 2),
            Cursor          = Cursors.Hand,
            Child           = g,
        };
        row.MouseEnter        += (_, _) => { if (!isSel) row.Background = hoverBg; };
        row.MouseLeave        += (_, _) => { if (!isSel) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) =>
        {
            _selectedOrphanPath = string.Equals(_selectedOrphanPath, path, StringComparison.OrdinalIgnoreCase)
                ? null : path;
            _selectedItemId = null;
            if (DetailDrawer.Visibility == Visibility.Visible) CloseDetailDrawer();
            BuildOrphansList();
            RefreshList();
            UpdateToolbarState();
        };
        return row;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }

    private void OpenDetailDrawer(CollectionItem item)
    {
        TxtDetailName.Text = "アイテム詳細";
        BuildDetailContent(item);

        bool wasOpen = FormDrawer.Visibility == Visibility.Visible || DrawerContainer.ActualWidth > 0;
        DetailDrawer.Visibility = Visibility.Visible;
        FormDrawer.Visibility   = Visibility.Collapsed;

        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = wasOpen ? DrawerContainer.ActualWidth : 0, To = 500,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
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
            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn },
        };
        anim.Completed += (_, _) =>
        {
            DetailDrawer.Visibility = Visibility.Collapsed;
            _selectedItemId = null;
            RefreshList();
            RefreshOrphans();
        };
        DrawerContainer.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e) => CloseDetailDrawer();

    private void BuildDetailContent(CollectionItem item)
    {
        DetailContentPanel.Children.Clear();

        foreach (var field in _col.Fields.OrderBy(f => f.Order))
        {
            var val = item.FieldValues.TryGetValue(field.Id, out var v) ? v : "";
            if (string.IsNullOrEmpty(val)) continue;

            if (field.FieldType == "ファイル")
                AddDetailFileRow(field.Name, val);
            else if (field.FieldType == "リンク")
                AddDetailLinkRow(field.Name, val);
            else if (field.InputFormat == "チェックボックス")
                AddDetailRow(field.Name, val == "true" ? "✓ 有効" : "未設定");
            else
                AddDetailRow(field.Name, val);
        }
        AddDetailRow("作成日時", item.AddedAt.ToString("yyyy/MM/dd HH:mm"));
        AddDetailRow("更新日時", item.UpdatedAt.ToString("yyyy/MM/dd HH:mm"));
    }

    private void AddDetailFileRow(string label, string path)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(new TextBlock
        {
            Text       = label,
            FontSize   = 11,
            Foreground = R<Brush>("TextDimBrush"),
            Margin     = new Thickness(0, 0, 0, 3),
        });
        sp.Children.Add(new TextBlock
        {
            Text         = Path.GetFileName(path),
            FontSize     = 13,
            Foreground   = R<Brush>("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            ToolTip      = path,
        });

        if (IsImageFile(path) && File.Exists(path))
        {
            var bmp = TryLoadBitmap(path);
            if (bmp != null)
            {
                var img = new Image { Source = bmp, Stretch = Stretch.Uniform };
                var expandIcon = new Border
                {
                    Width               = 28, Height = 28,
                    CornerRadius        = new CornerRadius(4),
                    Background          = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment   = VerticalAlignment.Bottom,
                    Margin              = new Thickness(0, 0, 6, 6),
                    Cursor              = Cursors.Hand,
                    Child               = new System.Windows.Shapes.Path
                    {
                        Data                = R<Geometry>("Bi.ArrowsFullscreen"),
                        Width               = 14, Height = 14,
                        Stretch             = Stretch.Uniform,
                        Fill                = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center,
                    },
                };
                var imgGrid = new Grid { Cursor = Cursors.Hand };
                imgGrid.Children.Add(img);
                imgGrid.Children.Add(expandIcon);
                imgGrid.MouseLeftButtonUp += (_, _) => OpenPreviewOverlay(bmp);

                sp.Children.Add(new Border
                {
                    Height       = 200,
                    CornerRadius = new CornerRadius(6),
                    ClipToBounds = true,
                    Margin       = new Thickness(0, 6, 0, 0),
                    Background   = R<Brush>("BgCardBrush"),
                    Child        = imgGrid,
                });
            }
        }

        DetailContentPanel.Children.Add(sp);
    }

    private void AddDetailRow(string label, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(new TextBlock
        {
            Text       = label,
            FontSize   = 11,
            Foreground = R<Brush>("TextDimBrush"),
            Margin     = new Thickness(0, 0, 0, 3),
        });
        sp.Children.Add(new TextBlock
        {
            Text         = value,
            FontSize     = 13,
            Foreground   = R<Brush>("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        DetailContentPanel.Children.Add(sp);
    }

    private void AddDetailLinkRow(string label, string url)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(new TextBlock
        {
            Text       = label,
            FontSize   = 11,
            Foreground = R<Brush>("TextDimBrush"),
            Margin     = new Thickness(0, 0, 0, 3),
        });
        sp.Children.Add(BuildLinkText(url, 13));
        DetailContentPanel.Children.Add(sp);
    }

    private TextBlock BuildLinkText(string url, double fontSize)
    {
        var tb = new TextBlock
        {
            FontSize          = fontSize,
            Foreground        = R<Brush>("AccentCyanBrush"),
            TextDecorations   = TextDecorations.Underline,
            Cursor            = Cursors.Hand,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            TextWrapping      = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip           = url,
        };
        tb.Inlines.Add(new System.Windows.Documents.Run(url));
        tb.MouseLeftButtonUp += (_, e) =>
        {
            OpenLink(url);
            e.Handled = true;
        };
        return tb;
    }

    private static void OpenLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = url,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    // ── ドラッグ＆ドロップ ──────────────────────────────────

    private void ContentArea_DragOver(object sender, DragEventArgs e)
    {
        if (FormDrawer.Visibility == Visibility.Visible) return;
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ContentArea_Drop(object sender, DragEventArgs e)
    {
        if (FormDrawer.Visibility == Visibility.Visible) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length == 0) return;

        var fileField = _col.Fields.FirstOrDefault(f => f.FieldType == "ファイル");
        if (fileField == null)
        {
            ShowForm(null);
            return;
        }

        foreach (var filePath in files)
        {
            if (!File.Exists(filePath)) continue;
            var copiedPath = CopyFileToCollection(filePath);
            var item = new CollectionItem
            {
                Name      = Path.GetFileNameWithoutExtension(filePath),
                AddedAt   = DateTime.Now,
                UpdatedAt = DateTime.Now,
            };
            item.FieldValues[fileField.Id] = copiedPath;
            _col.Items.Add(item);
        }
        _col.UpdatedAt = DateTime.Now;
        _vm.CollectionService.Save(_col);
        RefreshList();
        RefreshOrphans();
        e.Handled = true;
    }

    // ── グリッドヘルパー ────────────────────────────────────

    private Grid MakeRowGrid()
    {
        var g = new Grid();
        int colCount = Math.Max(_col.Fields.Count, 1);
        for (int i = 0; i < colCount; i++)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // 作成日時 / 更新日時
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    private static Thickness ColumnPadding(int col) =>
        new(col == 0 ? 0 : 18, 0, 18, 0);

    private void AddHeaderCell(Grid g, string text, int col)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = R<Brush>("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = ColumnPadding(col),
        };
        Grid.SetColumn(tb, col); g.Children.Add(tb);
    }

    private void AddCell(Grid g, string text, int col, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 13,
            Foreground = R<Brush>("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = ColumnPadding(col),
        };
        Grid.SetColumn(tb, col); g.Children.Add(tb);
    }

    // ── ツールバー ──────────────────────────────────────────

    private void UpdateToolbarState()
    {
        bool hasSel = _selectedItemId != null || _selectedOrphanPath != null;
        BtnEdit.IsEnabled   = hasSel;
        BtnDelete.IsEnabled = hasSel;
        BtnEdit.Opacity     = hasSel ? 1.0 : 0.35;
        BtnDelete.Opacity   = hasSel ? 1.0 : 0.35;
    }

    private bool HasFileField() => _col.Fields.Any(f => f.FieldType == "ファイル");

    private void AddItem_Click(object sender, RoutedEventArgs e) => ShowForm(null);

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrphanPath != null)
        {
            // 非管理アイテムを「編集」 = 情報付与してコレクションに取り込む
            var fileField = _col.Fields.FirstOrDefault(f => f.FieldType == "ファイル");
            if (fileField == null) return;
            var path = _selectedOrphanPath;
            ShowForm(null);
            _editingValues[fileField.Id] = path;
            BuildFormContent();
            return;
        }
        if (_selectedItemId == null) return;
        var item = _col.Items.FirstOrDefault(x => x.Id == _selectedItemId);
        if (item != null) ShowForm(item);
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrphanPath != null)
        {
            var path = _selectedOrphanPath;
            var fileName = Path.GetFileName(path);
            if (!AppDialog.Confirm($"非管理ファイル「{fileName}」をフォルダから削除しますか？\nこの操作は元に戻せません。",
                    "削除確認", Window.GetWindow(this), confirmLabel: "削除", dangerConfirm: true)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            _selectedOrphanPath = null;
            RefreshOrphans();
            UpdateToolbarState();
            return;
        }
        if (_selectedItemId == null) return;
        var item = _col.Items.FirstOrDefault(x => x.Id == _selectedItemId);
        if (item == null) return;

        var fileField = _col.Fields.FirstOrDefault(f => f.FieldType == "ファイル");
        string? filePath = null;
        if (fileField != null
            && item.FieldValues.TryGetValue(fileField.Id, out var fp)
            && !string.IsNullOrEmpty(fp)
            && File.Exists(fp))
            filePath = fp;

        var dlg = new ItemDeleteDialog(filePath) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        if (dlg.DeleteFile && filePath != null)
        {
            try { File.Delete(filePath); } catch { }
        }

        _col.Items.Remove(item);
        _col.UpdatedAt = DateTime.Now;
        _vm.CollectionService.Save(_col);
        _selectedItemId = null;
        if (DetailDrawer.Visibility == Visibility.Visible) CloseDetailDrawer();
        else { RefreshList(); RefreshOrphans(); }
    }

    // ── フォーム ────────────────────────────────────────────

    private void ShowForm(CollectionItem? existing)
    {
        _editingItemId = existing?.Id;
        _editingValues.Clear();

        TxtFormTitle.Text   = existing == null ? "アイテム追加" : "アイテム編集";
        BtnFormSave.Content = existing == null ? "追加" : "保存";

        if (existing != null)
            foreach (var kv in existing.FieldValues)
                _editingValues[kv.Key] = kv.Value;

        BuildFormContent();
        OpenFormDrawer();
    }

    private void BuildFormContent()
    {
        FormContentPanel.Children.Clear();

        if (_col.Fields.Count == 0)
        {
            FormContentPanel.Children.Add(new TextBlock
            {
                Text = "このコレクションにはデータ付属情報が定義されていません。\nコレクション一覧の編集からフィールドを追加してください。",
                FontSize = 12, Foreground = R<Brush>("TextDimBrush"), TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var field in _col.Fields.OrderBy(f => f.Order))
        {
            FormContentPanel.Children.Add(MakeFormLabel(field.Name));
            if (field.FieldType == "ファイル")
                FormContentPanel.Children.Add(BuildFileFieldRow(field));
            else if (field.InputFormat == "選択")
                FormContentPanel.Children.Add(BuildSelectFieldRow(field));
            else if (field.InputFormat == "チェックボックス")
                FormContentPanel.Children.Add(BuildToggleFieldRow(field));
            else
                FormContentPanel.Children.Add(MakeFormTextBox(field.Id, new Thickness(0, 0, 0, 14)));
        }
    }

    private UIElement BuildSelectFieldRow(CollectionField field)
    {
        var capField = field;
        _editingValues.TryGetValue(field.Id, out var current);
        var cb = new ComboBox
        {
            Style  = R<Style>("DarkComboBox"),
            Margin = new Thickness(0, 0, 0, 14),
        };
        foreach (var opt in field.SelectOptions)
            cb.Items.Add(new ComboBoxItem { Content = opt, Tag = opt });
        if (!string.IsNullOrEmpty(current))
        {
            for (int i = 0; i < cb.Items.Count; i++)
                if ((cb.Items[i] as ComboBoxItem)?.Tag as string == current)
                { cb.SelectedIndex = i; break; }
        }
        _editingValues.TryAdd(field.Id, "");
        cb.SelectionChanged += (_, _) =>
            _editingValues[capField.Id] = (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        return cb;
    }

    private UIElement BuildToggleFieldRow(CollectionField field)
    {
        var capField = field;
        _editingValues.TryGetValue(field.Id, out var current);
        bool on = current == "true";
        _editingValues[field.Id] = on ? "true" : "false";

        var offColor   = Color.FromRgb(80, 80, 80);
        var onColor    = Color.FromRgb(35, 131, 226);
        var trackBrush = new SolidColorBrush(on ? onColor : offColor);

        var thumb = new Border
        {
            Width             = 20, Height = 20,
            CornerRadius      = new CornerRadius(10),
            Background        = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin            = new Thickness(on ? 22 : 2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var track = new Border
        {
            Width        = 44, Height = 24,
            CornerRadius = new CornerRadius(12),
            Background   = trackBrush,
            Cursor       = Cursors.Hand,
            Child        = new Grid { Children = { thumb } },
        };

        bool animating = false;
        track.MouseLeftButtonUp += (_, _) =>
        {
            if (animating) return;
            animating = true;
            on = !on;
            _editingValues[capField.Id] = on ? "true" : "false";

            var thumbAnim = new System.Windows.Media.Animation.ThicknessAnimation
            {
                To             = new Thickness(on ? 22 : 2, 0, 0, 0),
                Duration       = TimeSpan.FromMilliseconds(180),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            };
            thumbAnim.Completed += (_, _) => animating = false;
            thumb.BeginAnimation(FrameworkElement.MarginProperty, thumbAnim);

            var colorAnim = new System.Windows.Media.Animation.ColorAnimation
            {
                To       = on ? onColor : offColor,
                Duration = TimeSpan.FromMilliseconds(180),
            };
            trackBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 14),
            Children    = { track },
        };
    }

    private UIElement BuildFileFieldRow(CollectionField field)
    {
        var capField = field;
        _editingValues.TryGetValue(field.Id, out var currentPath);

        var outerBorder = new Border
        {
            Margin          = new Thickness(0, 0, 0, 14),
            BorderBrush     = R<Brush>("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(8),
            Background      = R<Brush>("BgCardBrush"),
            AllowDrop       = true,
        };

        var pg = new Grid();
        pg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // パスではなく "ファイル名.拡張子" のみを表示
        var displayName = string.IsNullOrEmpty(currentPath)
            ? "" : Path.GetFileName(currentPath);

        var tb = new TextBox
        {
            Text       = displayName,
            IsReadOnly = true,
            Style      = R<Style>("DarkTextBox"),
            ToolTip    = string.IsNullOrEmpty(currentPath) ? null : currentPath,
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

        var hint = new TextBlock
        {
            Text       = "ファイルをここにドロップすることもできます",
            FontSize   = 11,
            Foreground = R<Brush>("TextDimBrush"),
            Margin     = new Thickness(0, 6, 0, 0),
        };

        var inner = new StackPanel();
        inner.Children.Add(pg);
        inner.Children.Add(hint);
        outerBorder.Child = inner;

        outerBorder.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        outerBorder.Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                _editingValues[capField.Id] = files[0];
                BuildFormContent();
            }
            e.Handled = true;
        };

        return outerBorder;
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
        if (FormDrawer.Visibility == Visibility.Visible) return;
        bool wasOpen = DetailDrawer.Visibility == Visibility.Visible || DrawerContainer.ActualWidth > 0;
        FormDrawer.Visibility   = Visibility.Visible;
        DetailDrawer.Visibility = Visibility.Collapsed;

        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = wasOpen ? DrawerContainer.ActualWidth : 0, To = 500,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        DrawerContainer.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void CloseFormDrawer()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = DrawerContainer.ActualWidth, To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn },
        };
        anim.Completed += (_, _) => FormDrawer.Visibility = Visibility.Collapsed;
        DrawerContainer.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void CancelForm_Click(object sender, RoutedEventArgs e)
    {
        _editingItemId = null;
        _editingValues.Clear();
        CloseFormDrawer();
    }

    private void SaveForm_Click(object sender, RoutedEventArgs e)
    {
        string DeriveLabel()
        {
            var first = _col.Fields.OrderBy(f => f.Order).FirstOrDefault();
            if (first == null) return "";
            if (!_editingValues.TryGetValue(first.Id, out var v) || string.IsNullOrWhiteSpace(v)) return "";
            return first.FieldType == "ファイル" ? Path.GetFileNameWithoutExtension(v) : v.Trim();
        }

        var now = DateTime.Now;
        if (_editingItemId == null)
        {
            var item = new CollectionItem { AddedAt = now, UpdatedAt = now };
            foreach (var field in _col.Fields)
            {
                if (!_editingValues.TryGetValue(field.Id, out var v) || string.IsNullOrEmpty(v)) continue;
                if (field.FieldType == "ファイル" && File.Exists(v))
                    v = CopyFileToCollection(v);
                item.FieldValues[field.Id] = v;
            }
            item.Name = DeriveLabel();
            _col.Items.Add(item);
        }
        else
        {
            var item = _col.Items.FirstOrDefault(x => x.Id == _editingItemId);
            if (item == null) return;
            item.FieldValues.Clear();
            foreach (var field in _col.Fields)
            {
                if (!_editingValues.TryGetValue(field.Id, out var v) || string.IsNullOrEmpty(v)) continue;
                if (field.FieldType == "ファイル" && File.Exists(v))
                    v = CopyFileToCollection(v);
                item.FieldValues[field.Id] = v;
            }
            item.Name      = DeriveLabel();
            item.UpdatedAt = now;
        }

        _col.UpdatedAt = DateTime.Now;
        _vm.CollectionService.Save(_col);
        _editingItemId = null;
        _editingValues.Clear();
        _selectedOrphanPath = null;
        CloseFormDrawer();
        RefreshList();
        RefreshOrphans();
    }

    // ── ファイルコピー ──────────────────────────────────────

    private string CopyFileToCollection(string sourcePath)
    {
        try
        {
            if (string.IsNullOrEmpty(_col.FolderPath) || !Directory.Exists(_col.FolderPath))
                return sourcePath;

            var destPath = Path.Combine(_col.FolderPath, Path.GetFileName(sourcePath));
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath),
                    StringComparison.OrdinalIgnoreCase))
                return sourcePath;

            if (File.Exists(destPath))
            {
                var baseName = Path.GetFileNameWithoutExtension(sourcePath);
                var ext      = Path.GetExtension(sourcePath);
                int n = 1;
                do { destPath = Path.Combine(_col.FolderPath, $"{baseName}_{n++}{ext}"); }
                while (File.Exists(destPath));
            }

            File.Copy(sourcePath, destPath);
            return destPath;
        }
        catch { return sourcePath; }
    }

    // ── 画像プレビューオーバーレイ ──────────────────────────

    private void OpenPreviewOverlay(BitmapImage bmp)
    {
        PreviewImage.Source = bmp;
        PreviewScale.ScaleX = PreviewScale.ScaleY = 1;
        PreviewTranslate.X  = PreviewTranslate.Y  = 0;
        UpdateZoomGauge(1);
        PreviewOverlay.Visibility = Visibility.Visible;
    }

    private void ClosePreviewOverlay()
    {
        _isPreviewDragging        = false;
        _previewPressedBackground = false;
        PreviewOverlay.ReleaseMouseCapture();
        PreviewOverlay.Visibility = Visibility.Collapsed;
        PreviewImage.Source       = null;
    }

    private void UpdateZoomGauge(double scale)
    {
        double ratio = Math.Clamp((scale - PreviewMinScale) / (PreviewMaxScale - PreviewMinScale), 0, 1);
        PreviewZoomLabel.Text = $"{(int)Math.Round(ratio * 100)}%";
        PreviewZoomFill.Width = ratio * PreviewGaugeWidth;
        ZoomThumb.Margin      = new Thickness(ratio * (PreviewGaugeWidth - PreviewThumbSize), 0, 0, 0);
    }

    private void SetZoomFromPoint(double x)
    {
        double ratio = Math.Clamp(x / PreviewGaugeWidth, 0, 1);
        double scale = PreviewMinScale + ratio * (PreviewMaxScale - PreviewMinScale);
        PreviewScale.ScaleX = PreviewScale.ScaleY = scale;
        UpdateZoomGauge(scale);
    }

    private void ClosePreview_Click(object sender, RoutedEventArgs e)
    {
        ClosePreviewOverlay();
        e.Handled = true;
    }

    private void PreviewOverlay_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)) return;
        double factor   = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        double newScale = Math.Clamp(PreviewScale.ScaleX * factor, PreviewMinScale, PreviewMaxScale);
        PreviewScale.ScaleX = PreviewScale.ScaleY = newScale;
        UpdateZoomGauge(newScale);
        e.Handled = true;
    }

    private void PreviewOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Image)
        {
            _isPreviewDragging     = true;
            _previewDragStart      = e.GetPosition(PreviewOverlay);
            _previewTranslateStart = new System.Windows.Point(PreviewTranslate.X, PreviewTranslate.Y);
            PreviewOverlay.CaptureMouse();
        }
        else
        {
            _previewPressedBackground = true;
        }
        e.Handled = true;
    }

    private void PreviewOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPreviewDragging) return;
        var pos = e.GetPosition(PreviewOverlay);
        PreviewTranslate.X = _previewTranslateStart.X + (pos.X - _previewDragStart.X);
        PreviewTranslate.Y = _previewTranslateStart.Y + (pos.Y - _previewDragStart.Y);
    }

    private void PreviewOverlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPreviewDragging)
        {
            _isPreviewDragging = false;
            PreviewOverlay.ReleaseMouseCapture();
        }
        else if (_previewPressedBackground)
        {
            _previewPressedBackground = false;
            ClosePreviewOverlay();
        }
        e.Handled = true;
    }

    private void ZoomTrack_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _zoomDragging = true;
        ZoomTrack.CaptureMouse();
        SetZoomFromPoint(e.GetPosition(ZoomTrack).X);
        e.Handled = true;
    }

    private void ZoomTrack_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_zoomDragging) return;
        SetZoomFromPoint(e.GetPosition(ZoomTrack).X);
    }

    private void ZoomTrack_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _zoomDragging = false;
        ZoomTrack.ReleaseMouseCapture();
        e.Handled = true;
    }

    // ── ユーティリティ ──────────────────────────────────────

    private static T? R<T>(string key) where T : class
    {
        try { return Application.Current.Resources[key] as T; }
        catch { return null; }
    }
}

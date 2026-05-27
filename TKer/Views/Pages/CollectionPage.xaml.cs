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
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>コレクションの一覧・詳細・作成・編集を管理するページ。</summary>
public partial class CollectionPage : Page, IRefreshable
{
    private enum SortMode { Name, Created, Updated }

    private readonly MainViewModel     _vm;
    private readonly CollectionService _svc;
    private string?  _selectedId;
    private bool     _isGridMode    = true;
    private SortMode _sortMode      = SortMode.Updated;
    private bool     _sortDescending = true;

    private readonly List<CollectionField>          _formFields       = new();
    private string?                                 _editingId;
    private string                                  _coverImageData   = string.Empty;
    private readonly Dictionary<string, BitmapImage?> _coverBitmapCache = new();
    private Window? _keyDownWindow;

    // ── 画像プレビューオーバーレイ ──
    private Point _previewDragStart;
    private Point _previewTranslateStart;
    private bool  _isPreviewDragging;
    private bool  _previewPressedBackground;
    private bool  _zoomDragging;
    private const double PreviewMinScale   = 1.0;
    private const double PreviewMaxScale   = 4.0;
    private const double PreviewGaugeWidth = 160.0;
    private const double PreviewThumbSize  = 14.0;

    /// <summary>コレクションページを初期化してデータを表示する。</summary>
    public CollectionPage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = vm.CollectionService;
        InitializeComponent();
        UpdateDisplayModeButtons();
        UpdateSortLabel();
        Refresh();

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
        ApplyFilter();
        UpdateDisplayModeButtons();
    }

    private void UpdateDisplayModeButtons()
    {
        ViewIconGrid.Visibility = _isGridMode ? Visibility.Visible   : Visibility.Collapsed;
        ViewIconList.Visibility = _isGridMode ? Visibility.Collapsed : Visibility.Visible;
        BtnViewToggle.ToolTip   = _isGridMode ? "リスト表示に切り替え" : "グリッド表示に切り替え";
    }

    // ── ソート ──────────────────────────────────────────────

    private static string SortModeName(SortMode mode) => mode switch
    {
        SortMode.Name    => "コレクション名",
        SortMode.Created => "作成日時",
        _                => "更新日時",
    };

    private void SortButton_Click(object sender, MouseButtonEventArgs e)
    {
        UpdateSortPopupHighlight();
        SortPopup.IsOpen = true;
    }

    private void SetSortMode(SortMode mode)
    {
        _sortMode = mode;
        UpdateSortLabel();
        SortPopup.IsOpen = false;
        ApplyFilter();
    }

    private void SortByName_Click(object sender, MouseButtonEventArgs e)    => SetSortMode(SortMode.Name);
    private void SortByCreated_Click(object sender, MouseButtonEventArgs e) => SetSortMode(SortMode.Created);
    private void SortByUpdated_Click(object sender, MouseButtonEventArgs e) => SetSortMode(SortMode.Updated);

    private void ToggleSortDirection_Click(object sender, MouseButtonEventArgs e)
    {
        _sortDescending = !_sortDescending;
        UpdateSortLabel();
        UpdateSortPopupHighlight();
        SortPopup.IsOpen = false;
        ApplyFilter();
    }

    private void UpdateSortLabel()
    {
        SortModeLabel.Text = $"{SortModeName(_sortMode)} {(_sortDescending ? "↓" : "↑")}";
    }

    private void UpdateSortPopupHighlight()
    {
        var active   = (Brush)Application.Current.Resources["AccentCyanBrush"];
        var inactive = (Brush)Application.Current.Resources["TextPrimaryBrush"];
        SortOptNameText.Foreground    = _sortMode == SortMode.Name    ? active : inactive;
        SortOptCreatedText.Foreground = _sortMode == SortMode.Created ? active : inactive;
        SortOptUpdatedText.Foreground = _sortMode == SortMode.Updated ? active : inactive;
        SortDirectionText.Text = _sortDescending ? "降順 ↓" : "昇順 ↑";
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

        IEnumerable<Collection> ordered = _sortMode switch
        {
            SortMode.Name    => _sortDescending ? list.OrderByDescending(c => c.Name)      : list.OrderBy(c => c.Name),
            SortMode.Created => _sortDescending ? list.OrderByDescending(c => c.CreatedAt) : list.OrderBy(c => c.CreatedAt),
            _                => _sortDescending ? list.OrderByDescending(c => c.UpdatedAt) : list.OrderBy(c => c.UpdatedAt),
        };
        list = ordered.ToList();

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
                BorderBrush     = Brush("AccentCyanBrush"),
            });

        // コレクション名オーバーレイ（下部）
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

        // ホバー時の白オーバーレイ（プロジェクト画面と同じ薄い輝き効果）
        var hoverOverlay = new Border
        {
            CornerRadius      = new CornerRadius(12),
            Background        = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible  = false,
            Visibility        = Visibility.Collapsed,
        };
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
            UpdateToolbarState();
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
        bool isSel = col.Id == _selectedId;

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(new TextBlock
        {
            Text              = col.Icon ?? "📁",
            FontSize          = 16,
            Margin            = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        namePanel.Children.Add(new TextBlock
        {
            Text              = col.Name,
            FontFamily        = new FontFamily("Yu Gothic UI"),
            FontSize          = 13,
            FontWeight        = FontWeights.SemiBold,
            Foreground        = Brush("TextPrimaryBrush"),
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(namePanel, 0); g.Children.Add(namePanel);

        var fmtBadge = new Border
        {
            Background          = new SolidColorBrush(Color.FromArgb(55, 35, 131, 226)),
            CornerRadius        = new CornerRadius(4),
            Padding             = new Thickness(8, 3, 8, 3),
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child               = new TextBlock
            {
                Text       = col.ItemFormat == "ファイル" ? "📁 ファイル" : "📝 文字列",
                FontSize   = 11,
                Foreground = Brush("AccentCyanBrush"),
            },
        };
        Grid.SetColumn(fmtBadge, 1); g.Children.Add(fmtBadge);

        var createdTb = new TextBlock
        {
            Text              = col.CreatedAt.ToString("yyyy/MM/dd HH:mm"),
            FontSize          = 11,
            Foreground        = Brush("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(createdTb, 2); g.Children.Add(createdTb);

        var updatedTb = new TextBlock
        {
            Text              = col.UpdatedAt.ToString("yyyy/MM/dd HH:mm"),
            FontSize          = 11,
            Foreground        = Brush("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(updatedTb, 3); g.Children.Add(updatedTb);

        var row = new Border
        {
            Background      = isSel ? Brush("BgSecondaryBrush") : Brushes.Transparent,
            Padding         = new Thickness(16, 10, 16, 10),
            Cursor          = Cursors.Hand,
            BorderBrush     = Brush("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child           = g,
        };
        row.MouseEnter        += (_, _) => { if (col.Id != _selectedId) row.Background = Brush("BgHoverBrush"); };
        row.MouseLeave        += (_, _) => { if (col.Id != _selectedId) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (_selectedId == col.Id && DetailDrawer.Visibility == Visibility.Visible)
            { CloseDetailDrawer(); return; }
            _selectedId = col.Id;
            ApplyFilter();
            UpdateToolbarState();
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
        BtnToolbarOpen.IsEnabled   = hasSel;
        BtnToolbarEdit.Opacity     = hasSel ? 1.0 : 0.35;
        BtnToolbarDelete.Opacity   = hasSel ? 1.0 : 0.35;
        BtnToolbarOpen.Opacity     = hasSel ? 1.0 : 0.35;
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
                    DeleteCollection_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
                case Key.O:
                    LoadCollection_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
                case Key.A:
                    if (e.OriginalSource is TextBox) break;
                    OpenCollection_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
            }
        }
        else if (ctrl && shift && !alt)
        {
            if (e.Key == Key.OemSemicolon)
            {
                NewCollection_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.F2)
            {
                EditCollection_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && PreviewOverlay.Visibility == Visibility.Visible)
            {
                ClosePreviewOverlay();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchSection.Visibility == Visibility.Visible)
            {
                ToggleSearch_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && DrawerContainer.ActualWidth > 0)
            {
                if (DetailDrawer.Visibility == Visibility.Visible)
                    CloseDetailDrawer();
                else
                    CloseFormDrawer();
                e.Handled = true;
            }
        }
    }

    // ── 詳細ドロワー ────────────────────────────────────────

    private void OpenDetailDrawer()
    {
        var col = _selectedId == null ? null : _svc.Collections.FirstOrDefault(c => c.Id == _selectedId);
        if (col == null) return;

        TxtDetailName.Text = col.Name;
        BuildDetailContent(col);

        if (DetailDrawer.Visibility == Visibility.Visible) return;

        // 切替前にドロワーが開いているか判定（フォーム→詳細ではしまわず内容だけ差し替える）
        bool wasOpen = FormDrawer.Visibility == Visibility.Visible || DrawerContainer.ActualWidth > 0;

        DetailDrawer.Visibility = Visibility.Visible;
        FormDrawer.Visibility   = Visibility.Collapsed;

        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = wasOpen ? DrawerContainer.ActualWidth : 0, To = 500,
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

    private void LoadCollection_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "コレクションファイルを選択",
            Filter = "コレクションファイル|*_collection.json|すべてのファイル|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var col  = Newtonsoft.Json.JsonConvert.DeserializeObject<Collection>(json);
            if (col == null) { AppDialog.ShowError("ファイルの読み込みに失敗しました", "エラー", Window.GetWindow(this)); return; }

            if (_svc.Collections.Any(c => c.Id == col.Id))
            {
                AppDialog.ShowWarning("このコレクションはすでに読み込まれています", "確認", Window.GetWindow(this));
                return;
            }

            _svc.Add(col);
            _selectedId = col.Id;
            ApplyFilter();
            UpdateToolbarState();
        }
        catch
        {
            AppDialog.ShowError("ファイルの読み込みに失敗しました", "エラー", Window.GetWindow(this));
        }
    }

    private void OpenCollection_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId == null) return;
        var col = _svc.Collections.FirstOrDefault(c => c.Id == _selectedId);
        if (col == null) return;
        _vm.SelectedCollection = col;
        _vm.NavigateToCommand.Execute("CollectionItems");
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

        var fmt    = existing?.ItemFormat ?? "文字列";
        bool isFile = fmt == "ファイル";
        RbItemText.IsChecked = !isFile;
        RbItemFile.IsChecked = isFile;
        FolderSection.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        if (CbFieldFile != null)
            CbFieldFile.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;

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
            "ファイル" => ("📁", "ファイル", Color.FromArgb(200, 120, 60, 200)),
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
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "保存先フォルダを選択" };
        if (dlg.ShowDialog() == true)
            TxtFormFolder.Text = dlg.FolderName;
    }

    private void ItemFormat_Changed(object sender, RoutedEventArgs e)
    {
        if (FolderSection == null || CbFieldType == null) return;
        bool isFile = RbItemFile.IsChecked == true;
        FolderSection.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;

        CbFieldFile.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        if (!isFile && (CbFieldType.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string == "ファイル")
            CbFieldType.SelectedIndex = 0;
    }

    private void SaveForm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtFormName.Text)) { TxtFormName.Focus(); return; }

        var itemFormat = RbItemFile.IsChecked == true ? "ファイル" : "文字列";

        if (itemFormat == "ファイル" && string.IsNullOrWhiteSpace(TxtFormFolder.Text))
        {
            AppDialog.ShowWarning("保存先フォルダを選択してください", "入力エラー", Window.GetWindow(this));
            TxtFormFolder.Focus();
            return;
        }

        for (int i = 0; i < _formFields.Count; i++)
            _formFields[i].Order = i;

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

        // ── ローカルヘルパー（プロジェクト詳細と同じスタイル） ──
        void AddSectionLine() =>
            DetailContentPanel.Children.Add(new Border
            {
                Height     = 1,
                Background = Brush("BorderBrush"),
            });

        void AddSectionTitle(string title) =>
            DetailContentPanel.Children.Add(new TextBlock
            {
                Text       = title,
                FontSize   = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("AccentCyanBrush"),
                Margin     = new Thickness(0, 10, 0, 10),
            });

        void AddHRow(string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text              = label,
                FontSize          = 13,
                Foreground        = Brush("TextDimBrush"),
                VerticalAlignment = VerticalAlignment.Top,
            };
            var val = new TextBlock
            {
                Text         = value,
                FontSize     = 13,
                Foreground   = Brush("TextPrimaryBrush"),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(val, 1);

            var copyIcon = new Border
            {
                Width             = 20, Height = 20,
                Margin            = new Thickness(4, 0, 0, 0),
                CornerRadius      = new CornerRadius(3),
                Cursor            = Cursors.Hand,
                Opacity           = 0,
                VerticalAlignment = VerticalAlignment.Top,
                Child             = new System.Windows.Shapes.Path
                {
                    Data                = Application.Current.Resources["Bi.ClipboardFill"] as Geometry,
                    Fill                = Brush("TextDimBrush"),
                    Stretch             = Stretch.Uniform,
                    Width               = 12, Height = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(copyIcon, 2);
            var captured = value;
            copyIcon.MouseLeftButtonUp += (_, _) => { Clipboard.SetText(captured); copyIcon.Opacity = 1; };
            g.MouseEnter += (_, _) => copyIcon.Opacity = 0.45;
            g.MouseLeave += (_, _) => copyIcon.Opacity = 0;

            g.Children.Add(lbl);
            g.Children.Add(val);
            g.Children.Add(copyIcon);
            DetailContentPanel.Children.Add(g);
        }

        // 表紙画像
        var coverBmp = TryGetCoverBitmap(col);
        if (coverBmp != null)
        {
            var capturedBmp = coverBmp;
            var previewIcon = new Border
            {
                Width               = 32,
                Height              = 32,
                CornerRadius        = new CornerRadius(4),
                Background          = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Bottom,
                Margin              = new Thickness(0, 0, 6, 6),
                Cursor              = Cursors.Hand,
                Child               = new System.Windows.Shapes.Path
                {
                    Data                = (Geometry)FindResource("Bi.ArrowsFullscreen"),
                    Width               = 16,
                    Height              = 16,
                    Stretch             = Stretch.Uniform,
                    Fill                = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                },
            };
            previewIcon.MouseLeftButtonUp += (_, _) => OpenPreviewOverlay(capturedBmp);

            var coverGrid = new Grid();
            coverGrid.Children.Add(new Image { Source = coverBmp, Stretch = Stretch.Uniform });
            coverGrid.Children.Add(previewIcon);

            DetailContentPanel.Children.Add(new Border
            {
                Height      = 180,
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Margin      = new Thickness(0, 0, 0, 16),
                Background  = Brush("BgCardBrush"),
                Child       = coverGrid,
            });
        }
        else
        {
            DetailContentPanel.Children.Add(new Border
            {
                Height          = 60,
                CornerRadius    = new CornerRadius(8),
                Background      = Brush("BgCardBrush"),
                BorderBrush     = Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(0, 0, 0, 16),
                Child           = new TextBlock
                {
                    Text                = "表紙画像は設定されていません",
                    FontSize            = 12,
                    Foreground          = Brush("TextDimBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                },
            });
        }

        // 詳細情報セクション
        AddSectionLine();
        AddSectionTitle("詳細情報");
        AddHRow("コレクション名", col.Name ?? "");
        if (!string.IsNullOrEmpty(col.Description))
            AddHRow("説明", col.Description);
        AddHRow("アイテム形式", col.ItemFormat == "ファイル" ? "📁 ファイル指定" : "📝 文字列");
        if (col.ItemFormat == "ファイル" && !string.IsNullOrEmpty(col.FolderPath))
            AddHRow("フォルダパス", col.FolderPath);
        AddHRow("作成日時", col.CreatedAt.ToString("yyyy/MM/dd HH:mm"));
        AddHRow("更新日時", col.UpdatedAt.ToString("yyyy/MM/dd HH:mm"));

        // データ付属情報フィールドセクション
        AddSectionLine();
        AddSectionTitle("データ付属情報フィールド");

        if (col.Fields.Count == 0)
        {
            DetailContentPanel.Children.Add(new TextBlock
            {
                Text         = "フィールドが定義されていません。ツールバーの「編集」から追加できます。",
                FontSize     = 12,
                Foreground   = Brush("TextDimBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var field in col.Fields.OrderBy(f => f.Order))
        {
            var (icon, label, badgeColor) = field.FieldType switch
            {
                "ファイル" => ("📁", "ファイル", Color.FromArgb(180, 120, 60, 200)),
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
                Text              = field.Name,
                FontSize          = 13,
                Foreground        = Brush("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(6, 0, 0, 0),
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

    // BrowseCoverImage_Click is no longer wired in XAML (button removed), kept for internal use if needed

    private void CoverImageArea_Click(object sender, MouseButtonEventArgs e) => OpenCoverImageDialog();

    private void CoverImage_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CoverImage_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CoverImage_DragLeave(object sender, DragEventArgs e) { }

    private void CoverImage_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0) LoadCoverImage(files[0]);
    }

    private void OpenCoverImageDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "表紙画像を選択",
            Filter = "画像ファイル|*.jpg;*.jpeg;*.png;*.bmp;*.gif|すべてのファイル|*.*",
        };
        if (dlg.ShowDialog() == true) LoadCoverImage(dlg.FileName);
    }

    private void LoadCoverImage(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
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
        catch { }
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

    // ── 画像プレビューオーバーレイ ──────────────────────────

    private void OpenPreviewOverlay(BitmapImage bmp)
    {
        PreviewImage.Source = bmp;
        PreviewScale.ScaleX = PreviewScale.ScaleY = 1;
        PreviewTranslate.X = PreviewTranslate.Y = 0;
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
            _previewTranslateStart = new Point(PreviewTranslate.X, PreviewTranslate.Y);
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

    private static Brush Brush(string key)
    {
        try { return (Brush)Application.Current.Resources[key]; }
        catch { return Brushes.Transparent; }
    }
}

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
    private readonly List<string>                   _draftSelectOptions = new();
    private string?                                 _editingId;
    private string                                  _coverImageData   = string.Empty;
    private readonly Dictionary<string, BitmapImage?> _coverBitmapCache = new();
    private Window? _keyDownWindow;
    private CollectionField? _dragField;

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
            // 表紙画像なし時のプレースホルダー（プロジェクト画面のグリッドと同じ Style を適用）
            grid.Children.Add(new System.Windows.Shapes.Path
            {
                Data  = Application.Current.Resources["Bi.Folder2Open"] as Geometry,
                Style = Application.Current.Resources["BiIconXl"] as Style,
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
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var namePanel = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 18, 0),
        };
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

        var createdTb = new TextBlock
        {
            Text              = col.CreatedAt.ToString("yyyy/MM/dd HH:mm"),
            FontSize          = 11,
            Foreground        = Brush("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 18, 0),
        };
        Grid.SetColumn(createdTb, 1); g.Children.Add(createdTb);

        var updatedTb = new TextBlock
        {
            Text              = col.UpdatedAt.ToString("yyyy/MM/dd HH:mm"),
            FontSize          = 11,
            Foreground        = Brush("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(updatedTb, 2); g.Children.Add(updatedTb);

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
        var (confirmed, deleteFolder) = ShowDeleteDialog(col);
        if (!confirmed) return;
        DetailDrawer.Visibility = Visibility.Collapsed;
        _svc.Delete(_selectedId, deleteFolder);
        _selectedId = null;
        ApplyFilter();
        UpdateToolbarState();
    }

    /// <summary>コレクション削除ダイアログを表示し、（実行するか, フォルダも削除するか）を返す。</summary>
    private (bool confirmed, bool deleteFolder) ShowDeleteDialog(Collection col)
    {
        bool hasFolder = col.Fields.Any(f => f.FieldType == "ファイル")
                         && !string.IsNullOrEmpty(col.FolderPath);
        var dlg = new CollectionDeleteDialog(col.Name ?? "", hasFolder ? col.FolderPath : null)
        {
            Owner = Window.GetWindow(this),
        };
        bool confirmed = dlg.ShowDialog() == true;
        return (confirmed, confirmed && dlg.DeleteFolder);
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
        // 検索欄を閉じても入力内容・検索結果は保持する
        SearchBarHelper.Toggle(SearchSection, SearchBox);
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
        var (confirmed, deleteFolder) = ShowDeleteDialog(col);
        if (!confirmed) return;
        if (DetailDrawer.Visibility == Visibility.Visible)
            DetailDrawer.Visibility = Visibility.Collapsed;
        _svc.Delete(_selectedId, deleteFolder);
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

            _svc.AddImported(col, dlg.FileName);
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

        TxtFormTitle.Text   = existing == null ? "コレクション追加" : "コレクション編集";
        BtnFormSave.Content = existing == null ? "作成" : "保存";
        TxtFormName.Text   = existing?.Name        ?? "";
        TxtFormDesc.Text   = existing?.Description ?? "";
        // 保存先フォルダ欄には「親フォルダ」を表示する（保存時に 親/コレクション名 を生成・移動する）
        TxtFormFolder.Text = existing != null && !string.IsNullOrEmpty(existing.FolderPath)
            ? (Path.GetDirectoryName(existing.FolderPath) ?? "")
            : "";

        if (existing != null)
            _formFields.AddRange(existing.Fields.Select(f => new CollectionField
            {
                Id = f.Id, Name = f.Name, FieldType = f.FieldType,
                InputFormat = string.IsNullOrEmpty(f.InputFormat) ? "入力" : f.InputFormat,
                SelectOptions = f.SelectOptions?.ToList() ?? new List<string>(),
                Order = f.Order,
            }));

        TxtNewFieldName.Text     = "";
        TxtNewSelectOption.Text  = "";
        CbFieldType.SelectedIndex   = 0;
        CbInputFormat.SelectedIndex = 0;
        _draftSelectOptions.Clear();
        RefreshSelectOptionsList();
        UpdateSelectOptionsVisibility();

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
        RefreshFolderSectionVisibility();
        OpenFormDrawer();
    }

    /// <summary>付属情報に「ファイル」型フィールドがあるかどうかを返す。</summary>
    private bool HasFileField() => _formFields.Any(f => f.FieldType == "ファイル");

    /// <summary>
    /// 「ファイル」型フィールドの有無に応じて保存先フォルダ欄の表示と、
    /// データ形式コンボの「ファイル」選択肢の有効/無効を切り替える。
    /// </summary>
    private void RefreshFolderSectionVisibility()
    {
        if (FolderSection == null) return;
        bool hasFile = HasFileField();
        FolderSection.Visibility = hasFile ? Visibility.Visible : Visibility.Collapsed;

        // ファイルフィールドは最大1つ。既にある場合は選択肢を隠す。
        if (CbFieldFile != null)
        {
            CbFieldFile.Visibility = hasFile ? Visibility.Collapsed : Visibility.Visible;
            if (hasFile && (CbFieldType.SelectedItem as ComboBoxItem)?.Tag as string == "ファイル")
                CbFieldType.SelectedIndex = 0;
        }
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

    private static (string label, Color color) FieldTypeBadge(string fieldType) => fieldType switch
    {
        "ファイル" => ("ファイル", Color.FromArgb(200, 120, 60, 200)),
        "リンク" => ("リンク", Color.FromArgb(200, 30, 140, 80)),
        _       => ("文字列", Color.FromArgb(200, 35, 100, 200)),
    };

    private UIElement BuildFormFieldRow(CollectionField field)
    {
        var (label, badgeColor) = FieldTypeBadge(field.FieldType);

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        var dragHandle = new TextBlock
        {
            Text              = "⋮⋮",
            FontSize          = 13,
            Foreground        = Brush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor            = Cursors.SizeAll,
        };
        Grid.SetColumn(dragHandle, 0); g.Children.Add(dragHandle);

        var nameTb = new TextBlock
        {
            Text = field.Name, FontSize = 13,
            Foreground = Brush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        Grid.SetColumn(nameTb, 1); g.Children.Add(nameTb);

        var inputBadge = new Border
        {
            Background        = new SolidColorBrush(Color.FromArgb(140, 90, 90, 110)),
            CornerRadius      = new CornerRadius(4),
            Padding           = new Thickness(7, 2, 7, 2),
            Margin            = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child             = new TextBlock { Text = field.InputFormat, FontSize = 11, Foreground = Brushes.White },
        };
        Grid.SetColumn(inputBadge, 2); g.Children.Add(inputBadge);

        var badge = new Border
        {
            Background        = new SolidColorBrush(badgeColor),
            CornerRadius      = new CornerRadius(4),
            Padding           = new Thickness(7, 2, 7, 2),
            Margin            = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child             = new TextBlock { Text = label, FontSize = 11, Foreground = Brushes.White },
        };
        Grid.SetColumn(badge, 3); g.Children.Add(badge);

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
        del.Click += (_, _) => { _formFields.Remove(cap); RefreshFormFieldList(); RefreshFolderSectionVisibility(); };
        Grid.SetColumn(del, 4); g.Children.Add(del);

        var rowBorder = new Border
        {
            Background      = Brush("BgSecondaryBrush"),
            BorderBrush     = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10, 7, 10, 7),
            Margin          = new Thickness(0, 0, 0, 5),
            Child           = g,
            AllowDrop       = true,
            Tag             = field,
        };

        // ドラッグ＆ドロップによる並べ替え
        rowBorder.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.GetPosition(rowBorder).X <= 24) _dragField = field;
        };
        rowBorder.MouseMove += (s, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragField == field)
                DragDrop.DoDragDrop(rowBorder, field, DragDropEffects.Move);
        };
        rowBorder.DragOver += (_, e) =>
        {
            e.Effects = _dragField != null ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        rowBorder.Drop += (_, e) =>
        {
            if (_dragField == null || _dragField == field) { _dragField = null; return; }
            int from = _formFields.IndexOf(_dragField);
            int to   = _formFields.IndexOf(field);
            if (from >= 0 && to >= 0)
            {
                _formFields.RemoveAt(from);
                _formFields.Insert(to, _dragField);
                for (int i = 0; i < _formFields.Count; i++) _formFields[i].Order = i;
                RefreshFormFieldList();
            }
            _dragField = null;
            e.Handled = true;
        };

        return rowBorder;
    }

    private void AddFormField_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtNewFieldName.Text.Trim();
        if (string.IsNullOrEmpty(name)) { TxtNewFieldName.Focus(); return; }
        var type        = (CbFieldType.SelectedItem   as ComboBoxItem)?.Tag as string ?? "文字列";
        var inputFormat = (CbInputFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "入力";

        // ファイル は「入力」固定
        if (type == "ファイル") inputFormat = "入力";

        // ファイル型フィールドは1つのみ
        if (type == "ファイル" && HasFileField())
        {
            AppDialog.ShowWarning("ファイル形式の付属情報は1つまでしか追加できません", "確認", Window.GetWindow(this));
            return;
        }

        if (inputFormat == "選択" && _draftSelectOptions.Count == 0)
        {
            AppDialog.ShowWarning("選択肢を1つ以上追加してください", "確認", Window.GetWindow(this));
            return;
        }

        _formFields.Add(new CollectionField
        {
            Name          = name,
            FieldType     = type,
            InputFormat   = inputFormat,
            SelectOptions = inputFormat == "選択" ? _draftSelectOptions.ToList() : new List<string>(),
            Order         = _formFields.Count,
        });
        TxtNewFieldName.Text = "";
        _draftSelectOptions.Clear();
        RefreshSelectOptionsList();
        TxtNewFieldName.Focus();
        RefreshFormFieldList();
        RefreshFolderSectionVisibility();
    }

    private void CbFieldType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CbInputFormat == null) return;
        var type = (CbFieldType.SelectedItem as ComboBoxItem)?.Tag as string ?? "文字列";
        // ファイル は入力形式が固定
        bool inputFormatLocked = type == "ファイル";
        CbInputFormat.IsEnabled = !inputFormatLocked;
        if (inputFormatLocked) CbInputFormat.SelectedIndex = 0;
        UpdateSelectOptionsVisibility();
    }

    private void CbInputFormat_Changed(object sender, SelectionChangedEventArgs e)
        => UpdateSelectOptionsVisibility();

    private void UpdateSelectOptionsVisibility()
    {
        if (SelectOptionsSection == null) return;
        var inputFormat = (CbInputFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "入力";
        SelectOptionsSection.Visibility = inputFormat == "選択" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddSelectOption_Click(object sender, RoutedEventArgs e)
    {
        var opt = TxtNewSelectOption.Text.Trim();
        if (string.IsNullOrEmpty(opt)) { TxtNewSelectOption.Focus(); return; }
        if (_draftSelectOptions.Contains(opt))
        {
            AppDialog.ShowWarning("同じ選択肢がすでに追加されています", "確認", Window.GetWindow(this));
            return;
        }
        _draftSelectOptions.Add(opt);
        TxtNewSelectOption.Text = "";
        TxtNewSelectOption.Focus();
        RefreshSelectOptionsList();
    }

    private void RefreshSelectOptionsList()
    {
        SelectOptionsList.Children.Clear();
        for (int i = 0; i < _draftSelectOptions.Count; i++)
        {
            var idx = i;
            var opt = _draftSelectOptions[i];
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tb = new TextBlock
            {
                Text              = opt,
                FontSize          = 12,
                Foreground        = Brush("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(tb, 0); g.Children.Add(tb);

            var del = new Button
            {
                Content         = "✕",
                FontSize        = 11,
                Background      = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground      = Brush("TextDimBrush"),
                Cursor          = Cursors.Hand,
                Padding         = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            del.Click += (_, _) =>
            {
                _draftSelectOptions.RemoveAt(idx);
                RefreshSelectOptionsList();
            };
            Grid.SetColumn(del, 1); g.Children.Add(del);

            SelectOptionsList.Children.Add(new Border
            {
                Background      = Brush("BgCardBrush"),
                BorderBrush     = Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(8, 4, 6, 4),
                Margin          = new Thickness(0, 0, 0, 4),
                Child           = g,
            });
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "保存先フォルダを選択" };
        if (dlg.ShowDialog() == true)
            TxtFormFolder.Text = dlg.FolderName;
    }

    private void SaveForm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtFormName.Text)) { TxtFormName.Focus(); return; }

        // 付属情報に「ファイル」型フィールドがある場合のみファイル形式扱い
        bool hasFile   = HasFileField();
        var itemFormat = hasFile ? "ファイル" : "文字列";

        if (hasFile && string.IsNullOrWhiteSpace(TxtFormFolder.Text))
        {
            AppDialog.ShowWarning("保存先フォルダを選択してください", "入力エラー", Window.GetWindow(this));
            TxtFormFolder.Focus();
            return;
        }

        for (int i = 0; i < _formFields.Count; i++)
            _formFields[i].Order = i;

        // 保存先フォルダ欄は「親フォルダ」。実際の生成先はサービスが 親/コレクション名 を組み立てる。
        var parentFolder = TxtFormFolder.Text.Trim();

        if (_editingId == null)
        {
            var col = new Collection
            {
                Name          = TxtFormName.Text.Trim(),
                Icon          = "📁",
                Description   = TxtFormDesc.Text.Trim(),
                ItemFormat    = itemFormat,
                Fields        = _formFields.ToList(),
                CoverImageData = _coverImageData,
            };
            try { _svc.Add(col, parentFolder); }
            catch (Exception ex)
            {
                AppDialog.ShowError($"保存先フォルダの作成に失敗しました\n{ex.Message}", "エラー", Window.GetWindow(this));
                return;
            }
            _selectedId = col.Id;
            _coverBitmapCache.Remove(col.Id);
        }
        else
        {
            var existing = _svc.Collections.FirstOrDefault(c => c.Id == _editingId);
            if (existing == null) return;

            // 再配置判定用に変更前の状態を控える（Fields はファイル形式判定に使う）
            var oldSnapshot = new Collection
            {
                Id = existing.Id, Name = existing.Name,
                ItemFormat = existing.ItemFormat, FolderPath = existing.FolderPath,
                Fields = existing.Fields.Select(f => new CollectionField
                {
                    Id = f.Id, Name = f.Name, FieldType = f.FieldType,
                    InputFormat = f.InputFormat, SelectOptions = f.SelectOptions?.ToList() ?? new List<string>(),
                    Order = f.Order,
                }).ToList(),
            };

            var updated = new Collection
            {
                Id             = existing.Id,
                Name           = TxtFormName.Text.Trim(),
                Icon           = existing.Icon,
                Description    = TxtFormDesc.Text.Trim(),
                ItemFormat     = itemFormat,
                Fields         = _formFields.ToList(),
                Items          = existing.Items,
                CoverImageData = _coverImageData,
                CreatedAt      = existing.CreatedAt,
                UpdatedAt      = DateTime.Now,
            };

            try { _svc.Update(updated, parentFolder, oldSnapshot); }
            catch (Exception ex)
            {
                AppDialog.ShowError($"保存先フォルダの変更に失敗しました\n{ex.Message}", "エラー", Window.GetWindow(this));
                return;
            }
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
        if (col.Fields.Any(f => f.FieldType == "ファイル") && !string.IsNullOrEmpty(col.FolderPath))
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
            AddDetailActionButtons(col);
            return;
        }

        foreach (var field in col.Fields.OrderBy(f => f.Order))
        {
            var (label, badgeColor) = FieldTypeBadge(field.FieldType);
            var inputLabel = string.IsNullOrEmpty(field.InputFormat) ? "入力" : field.InputFormat;

            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var fn = new TextBlock
            {
                Text              = field.Name,
                FontSize          = 13,
                Foreground        = Brush("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(fn, 0); g.Children.Add(fn);

            var inputBd = new Border
            {
                Background        = new SolidColorBrush(Color.FromArgb(140, 90, 90, 110)),
                CornerRadius      = new CornerRadius(4),
                Padding           = new Thickness(7, 2, 7, 2),
                Margin            = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child             = new TextBlock { Text = inputLabel, FontSize = 11, Foreground = Brushes.White },
            };
            Grid.SetColumn(inputBd, 1); g.Children.Add(inputBd);

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

        AddDetailActionButtons(col);
    }

    private void AddDetailActionButtons(Collection col)
    {
        // 区切り線（データ付属情報セクションの下）
        DetailContentPanel.Children.Add(new Border
        {
            Height     = 1,
            Background = Brush("BorderBrush"),
            Margin     = new Thickness(0, 4, 0, 0),
        });

        bool hasFolder = !string.IsNullOrEmpty(col.FolderPath) && Directory.Exists(col.FolderPath);

        var btnOpenItems = new Button
        {
            Content             = "アイテムを開く",
            Style               = (Style)FindResource("SecondaryButton"),
            Padding             = new Thickness(14, 7, 14, 7),
            Margin              = new Thickness(0, 0, 8, 0),
        };
        btnOpenItems.Click += (_, _) =>
        {
            _vm.SelectedCollection = col;
            _vm.NavigateToCommand.Execute("CollectionItems");
        };

        var btnExplorer = new Button
        {
            Content             = "エクスプローラーで開く",
            Style               = (Style)FindResource("SecondaryButton"),
            Padding             = new Thickness(14, 7, 14, 7),
            IsEnabled           = hasFolder,
            ToolTip             = hasFolder ? null : "コレクションフォルダが未設定です",
        };
        btnExplorer.Click += (_, _) => ShellHelper.OpenInExplorer(col.FolderPath);

        var panel = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin              = new Thickness(0, 12, 0, 4),
            Children            = { btnOpenItems, btnExplorer },
        };
        DetailContentPanel.Children.Add(panel);
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

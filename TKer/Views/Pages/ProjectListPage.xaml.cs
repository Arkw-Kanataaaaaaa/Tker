using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>プロジェクト一覧を表示・管理するページ。</summary>
public partial class ProjectListPage : Page, IRefreshable
{
    // カードのDataContext用。dynamic/匿名型はRuntimeBinderExceptionになるため名前付きクラスを使う
    private sealed class ProjectCardItem
    {
        public ProjectEntry Entry        { get; init; } = new();
        public int TotalTasks            { get; init; }
        public int DoneTasks             { get; init; }
        public int WipTasks              { get; init; }
        public int OverdueTasks          { get; init; }
        public double ProgressRate       { get; init; }
        public string ProgressLabel      { get; init; } = "";
        public bool HasAlert             { get; init; }
        public DateTime CreatedAt        { get; init; }
        public bool IsActive             { get; init; }
        public bool IsSelected           { get; init; }
        public BitmapImage? CoverBitmap  { get; init; }
        public bool HasNoCover           => CoverBitmap == null;
        public string HoverDetail        { get; init; } = "";
        public string PeriodLabel        { get; init; } = "";
        public string CreatedAtLabel     { get; init; } = "";
        public string UpdatedAtLabel     { get; init; } = "";
    }

    private readonly MainViewModel _vm;
    private string? _selectedPath;
    private string _coverImageData = string.Empty;
    private bool _isFolderManagementEnabled = true;
    private bool _isFolderMgmtToggleAnimating = false;
    private readonly SolidColorBrush _folderMgmtToggleBg = new(Color.FromRgb(35, 131, 226));
    private string _editCoverImageData = string.Empty;
    private bool _isEditFolderManagementEnabled = true;
    private bool _isEditFolderMgmtToggleAnimating = false;
    private readonly SolidColorBrush _editFolderMgmtToggleBg = new(Color.FromRgb(35, 131, 226));
    private string? _editingProjectPath;
    private ProjectData? _editingProjectData;
    private bool _isGridMode = true;
    private enum SortMode { Recent, Name, Progress, Created, Updated }
    private SortMode _sortMode = SortMode.Recent;
    private bool _sortDescending = true;
    private readonly Dictionary<string, BitmapImage?> _coverBitmapCache = new();

    /// <summary>コンストラクタ。ViewModelを受け取り初期化する。</summary>
    public ProjectListPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        FolderMgmtToggleSwitch.Background     = _folderMgmtToggleBg;
        EditFolderMgmtToggleSwitch.Background = _editFolderMgmtToggleBg;

        Loaded += (_, _) =>
        {
            UpdateDisplayModeButtons();
            UpdateSortLabel();
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

    private Window? _keyDownWindow;

    // ── IRefreshable ──────────────────────────────────────
    /// <summary>ページ全体のUIを最新データで再描画する。</summary>
    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("PL_Header"));
        ApplyFilter();
        ApplyBackground();
    }

    /// <summary>コンテンツボーダーの背景テーマを適用する。</summary>
    private void ApplyBackground()
    {
        UiThemeHelper.ApplyColorBackground(ContentBorder,
            _vm.AppSettingsService.ProjectListBgColor,
            _vm.AppSettingsService.ProjectListBgOpacity);
    }

    /// <summary>検索条件に基づいてプロジェクト一覧を絞り込んで表示する。</summary>
    private void ApplyFilter()
    {
        UpdateProjectToolbarState();

        var rawSummaries = _vm.AppSettingsService.CollectSummaries();
        IEnumerable<ProjectSummary> ordered = _sortMode switch
        {
            SortMode.Name     => _sortDescending ? rawSummaries.OrderByDescending(s => s.Entry.ProjectName)
                                                 : rawSummaries.OrderBy(s => s.Entry.ProjectName),
            SortMode.Progress => _sortDescending ? rawSummaries.OrderByDescending(s => s.ProgressRate)
                                                 : rawSummaries.OrderBy(s => s.ProgressRate),
            SortMode.Created  => _sortDescending ? rawSummaries.OrderByDescending(s => s.CreatedAt)
                                                 : rawSummaries.OrderBy(s => s.CreatedAt),
            SortMode.Updated  => _sortDescending ? rawSummaries.OrderByDescending(s => s.UpdatedAt)
                                                 : rawSummaries.OrderBy(s => s.UpdatedAt),
            _                 => _sortDescending ? rawSummaries.OrderByDescending(s => s.Entry.LastOpened)
                                                 : rawSummaries.OrderBy(s => s.Entry.LastOpened),
        };
        var summaries = ordered.ToList();
        var activeFilePath = _vm.ProjectService.ProjectFilePath;

        var q = SearchBox?.Text?.Trim().ToLower() ?? "";
        if (!string.IsNullOrEmpty(q))
            summaries = summaries.Where(s =>
                s.Entry.ProjectName.ToLower().Contains(q) ||
                s.Entry.Description.ToLower().Contains(q)).ToList();

        if (summaries.Count == 0)
        {
            NoProjectBanner.Visibility = Visibility.Visible;
            ProjectGridControl.ItemsSource = null;
            ProjectListItemsControl.ItemsSource = null;
        }
        else
        {
            NoProjectBanner.Visibility = Visibility.Collapsed;
            var items = summaries.Select(s => new ProjectCardItem
            {
                Entry           = s.Entry,
                TotalTasks      = s.TotalTasks,
                DoneTasks       = s.DoneTasks,
                WipTasks        = s.WipTasks,
                OverdueTasks    = s.OverdueTasks,
                ProgressRate    = s.ProgressRate,
                ProgressLabel   = s.ProgressLabel,
                HasAlert        = s.HasAlert,
                CreatedAt       = s.CreatedAt,
                IsActive        = s.Entry.DataFilePath == activeFilePath,
                IsSelected      = s.Entry.DataFilePath == _selectedPath,
                CoverBitmap     = TryGetCoverBitmap(s.Entry.DataFilePath),
                HoverDetail     = BuildHoverDetail(s),
                PeriodLabel     = BuildPeriodLabel(s.ProjectStartDate, s.ProjectEndDate),
                CreatedAtLabel  = s.CreatedAt.ToString("yyyy/MM/dd HH:mm"),
                UpdatedAtLabel  = s.UpdatedAt.ToString("yyyy/MM/dd HH:mm"),
            }).ToList();
            ProjectGridControl.ItemsSource = items;
            ProjectListItemsControl.ItemsSource = items;
        }
    }

    // ── カバー画像キャッシュ ──────────────────────────────
    /// <summary>プロジェクトファイルのカバー画像をキャッシュしながら取得する。</summary>
    private BitmapImage? TryGetCoverBitmap(string? jsonPath)
    {
        if (string.IsNullOrEmpty(jsonPath)) return null;
        if (_coverBitmapCache.TryGetValue(jsonPath, out var cached)) return cached;
        try
        {
            if (!File.Exists(jsonPath)) { _coverBitmapCache[jsonPath] = null; return null; }
            var json = File.ReadAllText(jsonPath);
            var pd = JsonConvert.DeserializeObject<ProjectData>(json);
            if (string.IsNullOrEmpty(pd?.Settings.CoverImageData))
            { _coverBitmapCache[jsonPath] = null; return null; }
            var bytes = Convert.FromBase64String(pd.Settings.CoverImageData);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _coverBitmapCache[jsonPath] = bmp;
            return bmp;
        }
        catch { _coverBitmapCache[jsonPath] = null; return null; }
    }

    /// <summary>ホバー時に表示するプロジェクト進捗の詳細テキストを生成する。</summary>
    private static string BuildHoverDetail(ProjectSummary s)
    {
        var lines = new List<string>();
        lines.Add($"完了 {s.DoneTasks} / {s.TotalTasks} タスク");
        if (s.WipTasks > 0)      lines.Add($"進行中 {s.WipTasks} 件");
        if (s.OverdueTasks > 0)  lines.Add($"⚠ 期限超過 {s.OverdueTasks} 件");
        return string.Join("\n", lines);
    }

    // ── ソート ──────────────────────────────────────────────
    private static string SortModeName(SortMode mode) => mode switch
    {
        SortMode.Name     => "プロジェクト名",
        SortMode.Progress => "進捗率",
        SortMode.Created  => "作成日時",
        SortMode.Updated  => "更新日時",
        _                 => "最近",
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

    private void SortByRecent_Click(object sender, MouseButtonEventArgs e)   => SetSortMode(SortMode.Recent);
    private void SortByName_Click(object sender, MouseButtonEventArgs e)     => SetSortMode(SortMode.Name);
    private void SortByProgress_Click(object sender, MouseButtonEventArgs e) => SetSortMode(SortMode.Progress);
    private void SortByCreated_Click(object sender, MouseButtonEventArgs e)  => SetSortMode(SortMode.Created);
    private void SortByUpdated_Click(object sender, MouseButtonEventArgs e)  => SetSortMode(SortMode.Updated);

    private void ToggleSortDirection_Click(object sender, MouseButtonEventArgs e)
    {
        _sortDescending = !_sortDescending;
        UpdateSortLabel();
        UpdateSortPopupHighlight();
        ApplyFilter();
    }

    private void UpdateSortLabel()
    {
        SortModeLabel.Text = $"{SortModeName(_sortMode)} {(_sortDescending ? "↓" : "↑")}";
    }

    private void UpdateSortPopupHighlight()
    {
        var active   = (Brush)FindResource("AccentCyanBrush");
        var inactive = (Brush)FindResource("TextPrimaryBrush");
        SortOptRecentText.Foreground   = _sortMode == SortMode.Recent   ? active : inactive;
        SortOptNameText.Foreground     = _sortMode == SortMode.Name     ? active : inactive;
        SortOptProgressText.Foreground = _sortMode == SortMode.Progress ? active : inactive;
        SortOptCreatedText.Foreground  = _sortMode == SortMode.Created  ? active : inactive;
        SortOptUpdatedText.Foreground  = _sortMode == SortMode.Updated  ? active : inactive;
        SortDirectionText.Text = _sortDescending ? "降順 ↓" : "昇順 ↑";
    }

    private static string BuildPeriodLabel(DateTime? start, DateTime? end)
    {
        if (start == null && end == null) return "─";
        var s = start?.ToString("yyyy/MM/dd") ?? "─";
        var e = end?.ToString("yyyy/MM/dd")   ?? "─";
        return $"{s} 〜 {e}";
    }

    // ── グリッド/リスト表示モード切替 ──────────────────────
    /// <summary>グリッド/リスト表示をトグルする。</summary>
    private void ToggleView_Click(object sender, MouseButtonEventArgs e)
    {
        _isGridMode = !_isGridMode;
        ProjectGridControl.Visibility = _isGridMode ? Visibility.Visible  : Visibility.Collapsed;
        ProjectListSection.Visibility = _isGridMode ? Visibility.Collapsed : Visibility.Visible;
        UpdateDisplayModeButtons();
    }

    /// <summary>グリッド/リスト切替アイコンを現在のモードに合わせて更新する。</summary>
    private void UpdateDisplayModeButtons()
    {
        ViewIconGrid.Visibility = _isGridMode ? Visibility.Visible   : Visibility.Collapsed;
        ViewIconList.Visibility = _isGridMode ? Visibility.Collapsed : Visibility.Visible;
        BtnViewToggle.ToolTip   = _isGridMode ? "リスト表示に切り替え" : "グリッド表示に切り替え";
    }

    /// <summary>フォルダ管理トグルのクリックで有効/無効を切り替える。</summary>
    private void ToggleFolderManagement_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isFolderMgmtToggleAnimating) return;
        _isFolderManagementEnabled = !_isFolderManagementEnabled;
        AnimateFolderMgmtToggle();
        FolderPathSection.Visibility = _isFolderManagementEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!_isFolderManagementEnabled)
            TxtNewProjPath.Clear();
    }

    /// <summary>フォルダ管理トグルのつまみと背景色をアニメーションで更新する。</summary>
    private void AnimateFolderMgmtToggle()
    {
        _isFolderMgmtToggleAnimating = true;

        var thumbAnim = new System.Windows.Media.Animation.ThicknessAnimation
        {
            To             = _isFolderManagementEnabled ? new Thickness(22, 0, 0, 0) : new Thickness(2, 0, 0, 0),
            Duration       = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        thumbAnim.Completed += (_, _) => _isFolderMgmtToggleAnimating = false;
        FolderMgmtThumb.BeginAnimation(MarginProperty, thumbAnim);

        var colorAnim = new System.Windows.Media.Animation.ColorAnimation
        {
            To       = _isFolderManagementEnabled ? Color.FromRgb(35, 131, 226) : Color.FromRgb(80, 80, 80),
            Duration = TimeSpan.FromMilliseconds(200)
        };
        _folderMgmtToggleBg.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
    }

    // ── 右パネル制御 ─────────────────────────────────────
    /// <summary>ドロワーを閉じて未選択状態にする。</summary>
    private void ShowEmptyPanel()
    {
        if (DrawerContainer.ActualWidth > 0) CloseDrawer();
    }

    /// <summary>プロジェクト詳細ドロワーを開いてプロジェクト情報を表示する。</summary>
    private void ShowInfoPanel()
    {
        var entry = _vm.AppSettingsService.RecentProjects.FirstOrDefault(p => p.DataFilePath == _selectedPath);
        DetailPanelTitle.Text = entry?.ProjectName ?? "";

        ProjectInfoContent.Children.Clear();

        if (entry == null) { OpenDetailDrawer(); return; }

        // タスク統計・設定を一度だけ読む
        ProjectData? projectData = null;
        try
        {
            if (File.Exists(_selectedPath))
            {
                var rawJson = File.ReadAllText(_selectedPath!);
                projectData = JsonConvert.DeserializeObject<ProjectData>(rawJson);
            }
        }
        catch { /* 読み込み失敗は無視 */ }

        // 表紙画像
        if (!string.IsNullOrEmpty(projectData?.Settings.CoverImageData))
        {
            try
            {
                var bytes = Convert.FromBase64String(projectData.Settings.CoverImageData);
                var bmp   = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                var capturedBmp = bmp;

                // プレビューアイコン（右下）
                var previewIcon = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new System.Windows.Shapes.Path
                    {
                        Data = (Geometry)FindResource("Bi.ArrowsFullscreen"),
                        Width = 16,
                        Height = 16,
                        Stretch = Stretch.Uniform,
                        Fill = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                previewIcon.MouseLeftButtonUp += (_, _) => OpenPreviewOverlay(capturedBmp);

                var coverGrid = new Grid();
                coverGrid.Children.Add(new Image { Source = bmp, Stretch = Stretch.Uniform });
                coverGrid.Children.Add(previewIcon);

                ProjectInfoContent.Children.Add(new Border
                {
                    Height        = 180,
                    CornerRadius  = new CornerRadius(8),
                    ClipToBounds  = true,
                    Margin        = new Thickness(0, 0, 0, 16),
                    Background     = (Brush)FindResource("BgCardBrush"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Child         = coverGrid
                });
            }
            catch { /* 画像デコード失敗は無視 */ }
        }
        else
        {
            // 表紙画像未設定時のプレースホルダー
            ProjectInfoContent.Children.Add(new Border
            {
                Height = 60, CornerRadius = new CornerRadius(8),
                Background = (Brush)FindResource("BgCardBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 16),
                Child = new TextBlock
                {
                    Text = "表紙画像は設定されていません",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextDimBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }

        // ── ローカルヘルパー ──
        void AddSectionLine() =>
            ProjectInfoContent.Children.Add(new Border
            {
                Height = 1, Background = (Brush)FindResource("BorderBrush"),
                Margin = new Thickness(0, 0, 0, 0)
            });

        void AddSectionTitle(string title) =>
            ProjectInfoContent.Children.Add(new TextBlock
            {
                Text = title, FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("AccentCyanBrush"),
                Margin = new Thickness(0, 10, 0, 10)
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
                Text = label, FontSize = 13,
                Foreground = (Brush)FindResource("TextDimBrush"),
                VerticalAlignment = VerticalAlignment.Top
            };
            var val = new TextBlock
            {
                Text = value, FontSize = 13,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(val, 1);

            var copyIcon = new Border
            {
                Width = 20, Height = 20, Margin = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(3), Cursor = System.Windows.Input.Cursors.Hand,
                Opacity = 0, VerticalAlignment = VerticalAlignment.Top,
                Child = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("Bi.ClipboardFill"),
                    Fill = (Brush)FindResource("TextDimBrush"),
                    Stretch = Stretch.Uniform, Width = 12, Height = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(copyIcon, 2);
            copyIcon.MouseLeftButtonUp += (_, _) =>
            {
                System.Windows.Clipboard.SetText(value);
                copyIcon.Opacity = 1;
            };

            g.MouseEnter += (_, _) => copyIcon.Opacity = 0.45;
            g.MouseLeave += (_, _) => copyIcon.Opacity = 0;

            g.Children.Add(lbl);
            g.Children.Add(val);
            g.Children.Add(copyIcon);
            ProjectInfoContent.Children.Add(g);
        }

        void AddProgressRow(string label, int pct)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock
            {
                Text = label, FontSize = 13,
                Foreground = (Brush)FindResource("TextDimBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var valStack = new StackPanel();
            valStack.Children.Add(new TextBlock
            {
                Text = $"{pct}%", FontSize = 13,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            var track = new Border
            {
                Height = 6, CornerRadius = new CornerRadius(3),
                Background = (Brush)FindResource("BgCardBrush")
            };
            var fill = new Border
            {
                Height = 6, CornerRadius = new CornerRadius(3),
                Background = (Brush)FindResource("AccentCyanBrush"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0  // set after layout via SizeChanged
            };
            var trackGrid = new Grid();
            trackGrid.Children.Add(track);
            trackGrid.Children.Add(fill);
            track.SizeChanged += (_, e) =>
                fill.Width = e.NewSize.Width * Math.Clamp(pct / 100.0, 0, 1);
            valStack.Children.Add(trackGrid);
            Grid.SetColumn(valStack, 1);
            g.Children.Add(lbl);
            g.Children.Add(valStack);
            ProjectInfoContent.Children.Add(g);
        }

        // ── 詳細情報セクション ──
        AddSectionLine();
        AddSectionTitle("詳細情報");
        AddHRow("プロジェクト名", entry.ProjectName ?? "");
        if (!string.IsNullOrWhiteSpace(entry.Description))
            AddHRow("説明", entry.Description);
        if (projectData?.Settings.UseFolderManagement == true && !string.IsNullOrEmpty(entry.ProjectPath))
            AddHRow("フォルダパス", entry.ProjectPath);
        if (projectData?.Settings.ProjectStartDate != null || projectData?.Settings.ProjectEndDate != null)
        {
            var start = projectData?.Settings.ProjectStartDate?.ToString("yyyy/MM/dd") ?? "─";
            var end   = projectData?.Settings.ProjectEndDate?.ToString("yyyy/MM/dd")   ?? "─";
            AddHRow("プロジェクト期間", $"{start}  〜  {end}");
        }
        if (projectData != null)
        {
            AddHRow("作成日時", projectData.Settings.CreatedAt.ToString("yyyy/MM/dd HH:mm"));
            AddHRow("更新日時", projectData.Settings.UpdatedAt.ToString("yyyy/MM/dd HH:mm"));
        }

        // ── タスク統計セクション（セクション間の線は1本） ──
        if (projectData != null)
        {
            var tasks    = projectData.Tasks;
            int total    = tasks.Count;
            int done     = tasks.Count(t => t.Status == "完了");
            int wip      = tasks.Count(t => t.Status == "進行中");
            int todo     = tasks.Count(t => t.Status == "未着手");
            int overdue  = tasks.Count(t => t.IsOverdue);
            int pct      = total > 0 ? (int)Math.Round(done * 100.0 / total) : 0;

            AddSectionLine();   // 詳細情報下 = タスク統計上 を兼ねる1本線
            AddSectionTitle("タスク統計");
            AddProgressRow("進捗率", pct);
            AddHRow("合計",     $"{total} 件");
            AddHRow("完了",     $"{done} 件");
            AddHRow("進行中",   $"{wip} 件");
            AddHRow("未着手",   $"{todo} 件");
            if (overdue > 0)
                AddHRow("期限超過", $"⚠ {overdue} 件");
            AddSectionLine();
        }
        else
        {
            AddSectionLine();   // タスクデータなし時の詳細情報下線
        }

        // ── エクスプローラーで開くボタン ──
        bool folderEnabled = projectData?.Settings.UseFolderManagement == true
                             && !string.IsNullOrEmpty(entry.ProjectPath);
        var btnExplorer = new Button
        {
            Content    = "エクスプローラーで開く",
            Style      = (Style)FindResource("SecondaryButton"),
            Padding    = new Thickness(14, 7, 14, 7),
            Margin     = new Thickness(0, 12, 0, 4),
            IsEnabled  = folderEnabled,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip    = folderEnabled ? null : "フォルダ管理が無効のため使用できません"
        };
        btnExplorer.Click += (_, _) => OpenFolder_Click(btnExplorer, new RoutedEventArgs());
        ProjectInfoContent.Children.Add(btnExplorer);

        OpenDetailDrawer();
    }


    /// <summary>プロジェクト詳細をプッシュ型ドロワーで表示する。</summary>
    private void OpenDetailDrawer() => OpenDrawer(DetailDrawerScroll);

    /// <summary>プロジェクト詳細ドロワーのクローズボタンのハンドラ。</summary>
    private void CloseDetailPanel_Click(object sender, RoutedEventArgs e) => CloseDetailDrawer();

    /// <summary>プロジェクト詳細ドロワーを閉じて未選択状態に戻す。</summary>
    private void CloseDetailDrawer()
        => CloseDrawer(() => { _selectedPath = null; ApplyFilter(); });

    private void OpenDrawer(FrameworkElement target)
    {
        bool wasOpen = DrawerContainer.ActualWidth > 0;

        AddDrawerScroll.Visibility    = target == AddDrawerScroll    ? Visibility.Visible : Visibility.Collapsed;
        EditDrawerScroll.Visibility   = target == EditDrawerScroll   ? Visibility.Visible : Visibility.Collapsed;
        DetailDrawerScroll.Visibility = target == DetailDrawerScroll ? Visibility.Visible : Visibility.Collapsed;

        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = wasOpen ? DrawerContainer.ActualWidth : 0, To = 500,
            Duration       = TimeSpan.FromMilliseconds(260),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        DrawerContainer.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void CloseDrawer(Action? onComplete = null)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = DrawerContainer.ActualWidth, To = 0,
            Duration       = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            AddDrawerScroll.Visibility    = Visibility.Collapsed;
            EditDrawerScroll.Visibility   = Visibility.Collapsed;
            DetailDrawerScroll.Visibility = Visibility.Collapsed;
            onComplete?.Invoke();
        };
        DrawerContainer.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    /// <summary>新規プロジェクト作成フォームをドロワーとして表示する。</summary>
    private void ShowAddProjectPanel()
    {
        // 選択中プロジェクトがあれば解除する
        _selectedPath = null;
        ApplyFilter();

        TxtNewProjName.Clear();
        TxtNewProjDesc.Clear();
        TxtNewProjPath.Clear();
        TxtNewProjStartDate.Clear();
        TxtNewProjEndDate.Clear();
        ClearCoverImageField();

        // フォルダ管理トグルをON状態にリセット
        _isFolderManagementEnabled   = true;
        _isFolderMgmtToggleAnimating = false;
        FolderMgmtThumb.BeginAnimation(MarginProperty, null);
        FolderMgmtThumb.Margin = new Thickness(22, 0, 0, 0);
        _folderMgmtToggleBg.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _folderMgmtToggleBg.Color    = Color.FromRgb(35, 131, 226);
        FolderPathSection.Visibility = Visibility.Visible;

        OpenDrawer(AddDrawerScroll);
        TxtNewProjName.Focus();
    }

    /// <summary>新規プロジェクト作成ドロワーを閉じる。</summary>
    private void HideAddProjectPanel() => CloseDrawer();

    // ─── 画像プレビュー オーバーレイ ───
    private Point _previewDragStart;
    private Point _previewTranslateStart;
    private bool _isPreviewDragging;
    private bool _previewPressedBackground;

    private const double PreviewMinScale   = 1.0;   // フィット = 0%
    private const double PreviewMaxScale   = 4.0;   // 最大ズーム = 100%
    private const double PreviewGaugeWidth = 160.0;
    private const double PreviewThumbSize  = 14.0;
    private bool _zoomDragging;

    /// <summary>表紙画像を全画面オーバーレイでプレビュー表示する。</summary>
    private void OpenPreviewOverlay(BitmapImage bmp)
    {
        PreviewImage.Source = bmp;
        PreviewScale.ScaleX = PreviewScale.ScaleY = 1;
        PreviewTranslate.X = PreviewTranslate.Y = 0;
        UpdateZoomGauge(1);
        PreviewOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>ズームゲージ・ツマミ・ラベルを現在の拡大率で更新する。割合は0〜100%で表示する。</summary>
    private void UpdateZoomGauge(double scale)
    {
        double ratio = Math.Clamp((scale - PreviewMinScale) / (PreviewMaxScale - PreviewMinScale), 0, 1);
        PreviewZoomLabel.Text = $"{(int)Math.Round(ratio * 100)}%";
        PreviewZoomFill.Width = ratio * PreviewGaugeWidth;
        ZoomThumb.Margin = new Thickness(ratio * (PreviewGaugeWidth - PreviewThumbSize), 0, 0, 0);
    }

    /// <summary>ゲージ上の位置から拡大率を設定する。</summary>
    private void SetZoomFromPoint(double x)
    {
        double ratio = Math.Clamp(x / PreviewGaugeWidth, 0, 1);
        double scale = PreviewMinScale + ratio * (PreviewMaxScale - PreviewMinScale);
        PreviewScale.ScaleX = PreviewScale.ScaleY = scale;
        UpdateZoomGauge(scale);
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

    private void ClosePreview_Click(object sender, RoutedEventArgs e)
    {
        ClosePreviewOverlay();
        e.Handled = true; // 下にある一覧ボタンへ MouseUp が伝播しないようにする
    }

    private void ClosePreviewOverlay()
    {
        _isPreviewDragging = false;
        _previewPressedBackground = false;
        PreviewOverlay.ReleaseMouseCapture();
        PreviewOverlay.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
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
            // 画像上はドラッグでパン
            _isPreviewDragging     = true;
            _previewDragStart      = e.GetPosition(PreviewOverlay);
            _previewTranslateStart = new Point(PreviewTranslate.X, PreviewTranslate.Y);
            PreviewOverlay.CaptureMouse();
        }
        else
        {
            // 背景押下。閉じる動作は MouseUp で行う（下のボタンへ伝播させないため）
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

    /// <summary>表紙画像フィールドをリセットする。</summary>
    private void ClearCoverImageField()
    {
        _coverImageData                  = string.Empty;
        CoverImagePreview.Source         = null;
        CoverImagePreview.Visibility     = Visibility.Collapsed;
        CoverImagePlaceholder.Visibility = Visibility.Visible;
        BtnClearCoverImage.Visibility    = Visibility.Collapsed;
    }

    /// <summary>プロジェクトツールバーボタンの有効/無効を更新する。</summary>
    private void UpdateProjectToolbarState()
    {
        bool hasSelection = !string.IsNullOrEmpty(_selectedPath);
        BtnSetActive.IsEnabled      = hasSelection;
        BtnOpenFolder.IsEnabled     = hasSelection;
        BtnEditSettings.IsEnabled   = hasSelection;
        BtnRemoveSelected.IsEnabled = hasSelection;
    }


    // ── キーボードショートカット ───────────────────────────
    /// <summary>ウィンドウ全体のキーボードショートカットを処理する。</summary>
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
                case Key.A:
                    if (e.OriginalSource is TextBox) break;
                    SetActive_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
                case Key.N:
                    LoadProject_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
                case Key.O:
                    OpenFolder_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
                case Key.F:
                    if (SearchSection.Visibility != Visibility.Visible)
                        ToggleSearch_Click(this, new RoutedEventArgs());
                    else { SearchBox.Focus(); SearchBox.SelectAll(); }
                    e.Handled = true; break;
                case Key.OemMinus:
                case Key.Subtract:
                    RemoveSelected_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
            }
        }
        else if (ctrl && shift && !alt)
        {
            if (e.Key == Key.OemSemicolon)
            {
                NewProject_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.Escape && PreviewOverlay.Visibility == Visibility.Visible)
            {
                ClosePreviewOverlay();
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                EditSettings_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchSection.Visibility == Visibility.Visible)
            {
                ToggleSearch_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && DrawerContainer.ActualWidth > 0)
            {
                if (DetailDrawerScroll.Visibility == Visibility.Visible)
                    CloseDetailDrawer();
                else
                    CloseDrawer();
                e.Handled = true;
            }
        }
    }

    // ── 検索 ──────────────────────────────────────────────
    /// <summary>検索バーの表示・非表示をトグルする。</summary>
    private void ToggleSearch_Click(object sender, RoutedEventArgs e)
        => SearchBarHelper.Toggle(SearchSection, SearchBox);

    /// <summary>検索テキスト変更時にフィルターを再適用する。</summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    // ── カード選択 ────────────────────────────────────────
    /// <summary>プロジェクトカードのクリックで選択・非選択を切り替える。ダブルクリックでアクティブに設定する。</summary>
    private void ProjectCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;

        FrameworkElement fe = (FrameworkElement)sender;
        if (fe.DataContext is not ProjectCardItem data) return;
        string path = data.Entry.DataFilePath ?? "";

        if (e.ClickCount == 2)
        {
            _selectedPath = path;
            SetActive_Click(this, new RoutedEventArgs());
            return;
        }

        if (_selectedPath == path)
        {
            // 同じカードをクリック → 選択解除
            _selectedPath = null;
            ApplyFilter();
            ShowEmptyPanel();
            return;
        }
        _selectedPath = path;
        ApplyFilter();

        ShowInfoPanel();
    }

    // ── ツールバー: プロジェクト管理 ─────────────────────
    /// <summary>選択中プロジェクトをアクティブに設定する。</summary>
    private void SetActive_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedPath))
        {
            AppDialog.ShowInfo("アクティブに設定するプロジェクトを選択してください", "確認", Window.GetWindow(this));
            return;
        }
        _vm.SwitchProjectCommand.Execute(_selectedPath);
        ApplyFilter();
        ShowInfoPanel();
    }

    /// <summary>選択中プロジェクトのフォルダをエクスプローラーで開く。</summary>
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = _selectedPath;
        if (string.IsNullOrEmpty(path))
        {
            AppDialog.ShowInfo("プロジェクトを選択してください", "確認", Window.GetWindow(this));
            return;
        }
        var entry = _vm.AppSettingsService.RecentProjects.FirstOrDefault(p => p.DataFilePath == path);
        var folder = entry?.ProjectPath ?? System.IO.Path.GetDirectoryName(path) ?? "";
        if (System.IO.Directory.Exists(folder))
            ShellHelper.OpenInExplorer(folder);
        else
            AppDialog.ShowError("フォルダが見つかりません", "エラー", Window.GetWindow(this));
    }

    /// <summary>カードのアクティブボタンからプロジェクトをアクティブに設定する。</summary>
    private void SetActiveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is string path)
        {
            _vm.SwitchProjectCommand.Execute(path);
            _selectedPath = path;
            Refresh();
        }
    }

    /// <summary>プロジェクト追加フォームパネルをインライン表示する。</summary>
    private void NewProject_Click(object sender, RoutedEventArgs e) => ShowAddProjectPanel();

    /// <summary>新規プロジェクト作成フォームの保存先フォルダ参照ダイアログを開く。</summary>
    private void BrowseNewProjPath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title           = "保存先フォルダを選択（そのままOKを押してください）",
            ValidateNames   = false,
            CheckFileExists = false,
            FileName        = "ここを変更せずOKを押してください",
            Filter          = "フォルダ|*.none"
        };
        if (dlg.ShowDialog() == true)
            TxtNewProjPath.Text = System.IO.Path.GetDirectoryName(dlg.FileName) ?? "";
    }

    private static readonly string[] _imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    /// <summary>表紙画像ファイルを選択して読み込みプレビュー表示する。</summary>
    private void BrowseCoverImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "表紙画像を選択",
            Filter = "画像ファイル|*.jpg;*.jpeg;*.png;*.bmp;*.gif|すべてのファイル|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        LoadCoverImage(dlg.FileName, isEdit: false);
    }

    /// <summary>指定ファイルを表紙画像として読み込みプレビューに反映する。</summary>
    private void LoadCoverImage(string filePath, bool isEdit)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            if (isEdit)
            {
                _editCoverImageData                  = Convert.ToBase64String(bytes);
                EditCoverImagePreview.Source         = bmp;
                EditCoverImagePreview.Visibility     = Visibility.Visible;
                EditCoverImagePlaceholder.Visibility = Visibility.Collapsed;
                BtnClearEditCoverImage.Visibility    = Visibility.Visible;
            }
            else
            {
                _coverImageData                  = Convert.ToBase64String(bytes);
                CoverImagePreview.Source         = bmp;
                CoverImagePreview.Visibility     = Visibility.Visible;
                CoverImagePlaceholder.Visibility = Visibility.Collapsed;
                BtnClearCoverImage.Visibility    = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"画像の読み込みに失敗しました:\n{ex.Message}", "エラー", Window.GetWindow(this));
        }
    }

    // ── 表紙画像 ドラッグ＆ドロップ ──────────────────────
    private void CoverImage_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border b && e.Data.GetDataPresent(DataFormats.FileDrop))
            b.BorderBrush = (Brush)FindResource("AccentCyanBrush");
    }

    private void CoverImage_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CoverImage_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b)
            b.BorderBrush = (Brush)FindResource("BorderBrush");
    }

    private void CoverImage_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border b) b.BorderBrush = (Brush)FindResource("BorderBrush");
        if (TryGetDroppedImagePath(e, out string path))
            LoadCoverImage(path, isEdit: false);
    }

    private void EditCoverImage_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border b) b.BorderBrush = (Brush)FindResource("BorderBrush");
        if (TryGetDroppedImagePath(e, out string path))
            LoadCoverImage(path, isEdit: true);
    }

    /// <summary>ドロップされたファイルが画像なら取得する。非画像なら警告を表示して false を返す。</summary>
    private bool TryGetDroppedImagePath(DragEventArgs e, out string path)
    {
        path = string.Empty;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return false;

        string file = files[0];
        string ext  = Path.GetExtension(file).ToLowerInvariant();
        if (!_imageExtensions.Contains(ext))
        {
            AppDialog.ShowWarning(
                $"画像ファイル（{string.Join(", ", _imageExtensions)}）をドロップしてください。\n" +
                $"対応していないファイル: {Path.GetFileName(file)}",
                "対応していないファイル形式", Window.GetWindow(this));
            return false;
        }
        path = file;
        return true;
    }

    /// <summary>選択中の表紙画像をクリアする。</summary>
    private void ClearCoverImage_Click(object sender, RoutedEventArgs e) => ClearCoverImageField();

    /// <summary>新規プロジェクト作成フォームの入力値を検証してプロジェクトを作成する。</summary>
    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtNewProjName.Text.Trim();
        var desc = TxtNewProjDesc.Text.Trim();
        var path = TxtNewProjPath.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            AppDialog.ShowWarning("プロジェクト名を入力してください", "入力エラー", Window.GetWindow(this));
            TxtNewProjName.Focus();
            return;
        }
        if (_isFolderManagementEnabled && string.IsNullOrWhiteSpace(path))
        {
            AppDialog.ShowWarning("保存先フォルダを選択してください", "入力エラー", Window.GetWindow(this));
            return;
        }

        bool useFolder = _isFolderManagementEnabled;

        DateTime? startDate = null, endDate = null;
        if (DateTime.TryParse(TxtNewProjStartDate.Text.Trim(), out var sd)) startDate = sd;
        if (DateTime.TryParse(TxtNewProjEndDate.Text.Trim(), out var ed)) endDate = ed;

        _vm.ProjectService.CreateProject(path, name, desc, useFolder, _coverImageData, startDate, endDate);

        var customPresets = _vm.AppSettingsService.Settings.CategoryPresets;
        var templateDlg = new CategoryTemplateDialog(customPresets) { Owner = Window.GetWindow(this) };
        if (templateDlg.ShowDialog() == true)
        {
            foreach (var item in templateDlg.SelectedCategories)
                _vm.ProjectService.AddCategory(item.Name, item.Description, item.Color);
        }

        _coverBitmapCache.Clear();
        ApplyFilter();
        HideAddProjectPanel();
    }

    /// <summary>新規プロジェクト作成ドロワーをキャンセルして閉じる。</summary>
    private void CancelAddProject_Click(object sender, RoutedEventArgs e)
        => HideAddProjectPanel();

    /// <summary>既存プロジェクトファイルを読み込むダイアログを表示する。</summary>
    private void LoadProject_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "プロジェクトファイルを読み込む",
            Filter = "TKer データ (*.json)|*.json|すべてのファイル (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            if (_vm.ProjectService.LoadProject(dlg.FileName))
                Refresh();
            else
                AppDialog.ShowError("プロジェクトを開けませんでした", "エラー", Window.GetWindow(this));
        }
    }

    /// <summary>プロジェクト編集ドロワーを表示する。</summary>
    private void EditSettings_Click(object sender, RoutedEventArgs e)
    {
        var path = _selectedPath;
        if (string.IsNullOrEmpty(path))
        {
            AppDialog.ShowInfo("編集するプロジェクトを選択してください", "確認", Window.GetWindow(this));
            return;
        }

        ProjectData? project;
        if (path == _vm.ProjectService.ProjectFilePath)
            project = _vm.ProjectService.CurrentProject;
        else
        {
            try   { project = JsonConvert.DeserializeObject<ProjectData>(File.ReadAllText(path)); }
            catch { project = null; }
        }

        if (project == null)
        { AppDialog.ShowError("プロジェクトを開けませんでした", "エラー", Window.GetWindow(this)); return; }

        ShowEditProjectPanel(project, path);
    }

    private void ShowEditProjectPanel(ProjectData project, string path)
    {
        _editingProjectPath = path;
        _editingProjectData = project;

        // フィールドを既存値で初期化
        TxtEditProjName.Text    = project.Settings.ProjectName;
        TxtEditProjDesc.Text    = project.Settings.Description;
        TxtEditProjPath.Text    = project.Settings.ProjectPath ?? "";
        TxtEditProjStartDate.Text = project.Settings.ProjectStartDate?.ToString("yyyy/MM/dd") ?? "";
        TxtEditProjEndDate.Text   = project.Settings.ProjectEndDate?.ToString("yyyy/MM/dd") ?? "";


        // 表紙画像
        _editCoverImageData = project.Settings.CoverImageData ?? "";
        if (!string.IsNullOrEmpty(_editCoverImageData))
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new System.IO.MemoryStream(Convert.FromBase64String(_editCoverImageData));
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                EditCoverImagePreview.Source          = bmp;
                EditCoverImagePreview.Visibility      = Visibility.Visible;
                EditCoverImagePlaceholder.Visibility  = Visibility.Collapsed;
                BtnClearEditCoverImage.Visibility     = Visibility.Visible;
            }
            catch { ClearEditCoverImageField(); }
        }
        else
        {
            ClearEditCoverImageField();
        }

        // フォルダ管理トグル
        _isEditFolderManagementEnabled   = project.Settings.UseFolderManagement;
        _isEditFolderMgmtToggleAnimating = false;
        EditFolderMgmtThumb.BeginAnimation(MarginProperty, null);
        EditFolderMgmtThumb.Margin = _isEditFolderManagementEnabled
            ? new Thickness(22, 0, 0, 0) : new Thickness(2, 0, 0, 0);
        _editFolderMgmtToggleBg.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, null);
        _editFolderMgmtToggleBg.Color = _isEditFolderManagementEnabled
            ? Color.FromRgb(35, 131, 226) : Color.FromRgb(80, 80, 80);
        EditFolderPathSection.Visibility = _isEditFolderManagementEnabled
            ? Visibility.Visible : Visibility.Collapsed;

        OpenDrawer(EditDrawerScroll);
        TxtEditProjName.Focus();
    }

    private void HideEditProjectPanel() => CloseDrawer();

    private void ClearEditCoverImageField()
    {
        _editCoverImageData                     = string.Empty;
        EditCoverImagePreview.Source            = null;
        EditCoverImagePreview.Visibility        = Visibility.Collapsed;
        EditCoverImagePlaceholder.Visibility    = Visibility.Visible;
        BtnClearEditCoverImage.Visibility       = Visibility.Collapsed;
    }

    private void BrowseEditCoverImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "表紙画像を選択",
            Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        LoadCoverImage(dlg.FileName, isEdit: true);
    }

    private void ClearEditCoverImage_Click(object sender, RoutedEventArgs e) => ClearEditCoverImageField();

    private void BrowseEditProjPath_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description         = "保存先フォルダを選択してください",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtEditProjPath.Text = dlg.SelectedPath;
    }

    private void ToggleEditFolderManagement_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isEditFolderMgmtToggleAnimating) return;
        _isEditFolderManagementEnabled = !_isEditFolderManagementEnabled;
        AnimateEditFolderMgmtToggle();
        EditFolderPathSection.Visibility = _isEditFolderManagementEnabled
            ? Visibility.Visible : Visibility.Collapsed;
        if (!_isEditFolderManagementEnabled)
            TxtEditProjPath.Clear();
    }

    private void AnimateEditFolderMgmtToggle()
    {
        _isEditFolderMgmtToggleAnimating = true;

        var thumbAnim = new System.Windows.Media.Animation.ThicknessAnimation
        {
            To = _isEditFolderManagementEnabled ? new Thickness(22, 0, 0, 0) : new Thickness(2, 0, 0, 0),
            Duration       = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        thumbAnim.Completed += (_, _) => _isEditFolderMgmtToggleAnimating = false;
        EditFolderMgmtThumb.BeginAnimation(MarginProperty, thumbAnim);

        var colorAnim = new System.Windows.Media.Animation.ColorAnimation
        {
            To       = _isEditFolderManagementEnabled ? Color.FromRgb(35, 131, 226) : Color.FromRgb(80, 80, 80),
            Duration = TimeSpan.FromMilliseconds(200)
        };
        _editFolderMgmtToggleBg.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, colorAnim);
    }

    private void SaveEditProject_Click(object sender, RoutedEventArgs e)
    {
        var project = _editingProjectData;
        var path    = _editingProjectPath;
        if (project == null || string.IsNullOrEmpty(path)) return;

        var name = TxtEditProjName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AppDialog.ShowWarning("プロジェクト名を入力してください", "入力エラー", Window.GetWindow(this));
            TxtEditProjName.Focus();
            return;
        }

        project.Settings.ProjectName        = name;
        project.Settings.Description        = TxtEditProjDesc.Text.Trim();
        project.Settings.UseFolderManagement = _isEditFolderManagementEnabled;
        project.Settings.ProjectPath        = TxtEditProjPath.Text.Trim();
        project.Settings.CoverImageData     = _editCoverImageData;
        project.Settings.UpdatedAt          = DateTime.Now;

        if (DateTime.TryParse(TxtEditProjStartDate.Text.Trim(), out var sd)) project.Settings.ProjectStartDate = sd;
        else project.Settings.ProjectStartDate = null;
        if (DateTime.TryParse(TxtEditProjEndDate.Text.Trim(), out var ed)) project.Settings.ProjectEndDate = ed;
        else project.Settings.ProjectEndDate = null;

        if (path == _vm.ProjectService.ProjectFilePath)
        {
            _vm.ProjectService.SaveProject();
        }
        else
        {
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(project, Formatting.Indented));
                _vm.AppSettingsService.RegisterProject(path, name, project.Settings.ProjectPath, name);
                _vm.AppSettingsService.CollectSummaries(forceRefresh: true);
            }
            catch { }
        }

        _coverBitmapCache.Clear();
        Refresh();
        HideEditProjectPanel();
    }

    private void CancelEditProject_Click(object sender, RoutedEventArgs e) => HideEditProjectPanel();

    // ── 削除 ──────────────────────────────────────────────
    /// <summary>選択中プロジェクトの削除ダイアログを表示する。</summary>
    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var path = _selectedPath;
        if (string.IsNullOrEmpty(path))
        {
            AppDialog.ShowInfo("削除するプロジェクトを選択してください", "確認", Window.GetWindow(this));
            return;
        }
        _ = ShowDeleteDialogAsync(path);
    }

    /// <summary>カードの削除ボタンからプロジェクト削除ダイアログを表示する。</summary>
    private void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string path) return;
        _ = ShowDeleteDialogAsync(path);
    }

    /// <summary>プロジェクト削除確認ダイアログを表示して削除を実行する。</summary>
    private async Task ShowDeleteDialogAsync(string path)
    {
        var entry = _vm.AppSettingsService.RecentProjects.FirstOrDefault(p => p.DataFilePath == path);
        var projectName   = entry?.ProjectName ?? System.IO.Path.GetFileName(path);
        var projectFolder = entry?.ProjectPath ?? System.IO.Path.GetDirectoryName(path) ?? "";

        var bg  = (Brush)FindResource("BgCardBrush");
        var fg  = (Brush)FindResource("TextPrimaryBrush");
        var dim = (Brush)FindResource("TextDimBrush");

        var win = new Window
        {
            Title = "プロジェクトの削除", Width = 440, Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = bg
        };
        var sp = new StackPanel { Margin = new Thickness(24) };
        sp.Children.Add(new TextBlock
        {
            Text = $"「{projectName}」を一覧から削除しますか？",
            Foreground = fg, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        var chkFolder = new CheckBox
        {
            Content = "プロジェクトフォルダも削除する",
            Foreground = fg, FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };
        sp.Children.Add(chkFolder);
        sp.Children.Add(new TextBlock
        {
            Text = projectFolder,
            Foreground = dim, FontSize = 10, FontFamily = new FontFamily("Consolas"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(22, 0, 0, 20)
        });
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var btnCancel = new Button
        {
            Content = "キャンセル", Style = (Style)FindResource("SecondaryButton"),
            Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0)
        };
        var btnOk = new Button
        {
            Content = "削除", Style = (Style)FindResource("DangerButton"),
            Padding = new Thickness(20, 6, 20, 6)
        };
        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnOk.Click     += (_, _) => win.DialogResult = true;
        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        sp.Children.Add(btnPanel);
        win.Content = sp;
        win.PreviewKeyDown += (_, ev) =>
        {
            if (ev.Key == Key.Escape) { win.DialogResult = false; ev.Handled = true; }
        };

        if (win.ShowDialog() != true) return;

        bool deleteFolder = chkFolder.IsChecked == true;
        _vm.AppSettingsService.RemoveProject(path);
        if (_selectedPath == path)
        {
            _selectedPath = null;
            ShowEmptyPanel();
        }
        _coverBitmapCache.Clear();
        Refresh();

        if (deleteFolder && !string.IsNullOrEmpty(projectFolder) && Directory.Exists(projectFolder))
            await DeleteFolderWithProgressAsync(projectFolder, projectName);
    }

    /// <summary>プロジェクトフォルダをプログレスバー付きで非同期削除する。</summary>
    private async Task DeleteFolderWithProgressAsync(string folderPath, string projectName)
    {
        DeletionArea.Visibility    = Visibility.Visible;
        TxtDeletionProject.Text    = $"削除中: {projectName}";
        DeletionProgressBar.Value  = 0;
        TxtDeletionPct.Text        = "0%";

        try
        {
            var progress = new Progress<double>(pct =>
            {
                DeletionProgressBar.Value = pct;
                TxtDeletionPct.Text       = $"{(int)pct}%";
            });

            await Task.Run(() =>
            {
                string[] files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                int total = files.Length;
                int done  = 0;
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { }
                    done++;
                    double pct = total > 0 ? (double)done / total * 100 : 100;
                    ((IProgress<double>)progress).Report(pct);
                }
                try { Directory.Delete(folderPath, true); } catch { }
            });

            DeletionProgressBar.Value = 100;
            TxtDeletionPct.Text       = "100%";
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            TxtDeletionProject.Text = $"エラー: {ex.Message}";
            await Task.Delay(2000);
        }
        finally
        {
            DeletionArea.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>フォルダ整理ダイアログを表示して未紐づけファイルを一括移動する。</summary>
    private void Organize_Click(object sender, RoutedEventArgs e)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) { AppDialog.ShowInfo("プロジェクトを開いてください", "確認", Window.GetWindow(this)); return; }

        var rootPath = project.Settings.ProjectPath;
        if (!Directory.Exists(rootPath)) { AppDialog.ShowInfo("プロジェクトフォルダが見つかりません", "確認", Window.GetWindow(this)); return; }

        var misplaced = new List<(string FilePath, string Reason)>();

        var linkedCatPaths = new HashSet<string>(
            project.Categories
                .Where(c => !string.IsNullOrEmpty(c.FolderPath) && Directory.Exists(c.FolderPath))
                .Select(c => Path.GetFullPath(c.FolderPath)),
            StringComparer.OrdinalIgnoreCase);

        var linkedTaskPaths = new HashSet<string>(
            project.Tasks
                .Where(t => !string.IsNullOrEmpty(t.FolderPath) && Directory.Exists(t.FolderPath))
                .Select(t => Path.GetFullPath(t.FolderPath)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var f in Directory.GetFiles(rootPath))
        {
            var ext = Path.GetExtension(f);
            if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase)) continue;
            if (f.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase)) continue;
            misplaced.Add((f, "プロジェクトルート直下（ファイル）"));
        }
        foreach (var d in Directory.GetDirectories(rootPath))
        {
            var full = Path.GetFullPath(d);
            if (linkedCatPaths.Contains(full)) continue;
            if (Path.GetFileName(d) == "作業完了") continue;
            misplaced.Add((d, "プロジェクトルート直下（未紐づけフォルダ）"));
        }

        foreach (var cat in project.Categories)
        {
            if (!Directory.Exists(cat.FolderPath)) continue;
            foreach (var f in Directory.GetFiles(cat.FolderPath))
                misplaced.Add((f, $"カテゴリー直下ファイル ({cat.Name})"));
            foreach (var d in Directory.GetDirectories(cat.FolderPath))
            {
                var full = Path.GetFullPath(d);
                if (linkedTaskPaths.Contains(full)) continue;
                misplaced.Add((d, $"カテゴリー直下・未紐づけフォルダ ({cat.Name})"));
            }
        }

        if (misplaced.Count == 0)
        {
            AppDialog.ShowInfo("整理が必要なファイルはありません ✅", "フォルダ整理", Window.GetWindow(this));
            return;
        }

        ShowOrganizeDialog(misplaced, project, rootPath);
    }

    /// <summary>カテゴリテンプレート一覧ダイアログを表示し、選択されたカテゴリをプロジェクトに追加する。</summary>
    private void CategoryTemplate_Click(object sender, RoutedEventArgs e)
    {
        var customPresets = _vm.AppSettingsService.Settings.CategoryPresets;
        var dlg = new TKer.Views.Dialogs.CategoryTemplateDialog(customPresets)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() != true) return;

        if (_vm.ProjectService.CurrentProject == null)
        {
            AppDialog.ShowInfo("カテゴリを追加するにはプロジェクトをアクティブにしてください", "確認", Window.GetWindow(this));
            return;
        }

        foreach (var item in dlg.SelectedCategories)
            _vm.ProjectService.AddCategory(item.Name, item.Description, item.Color);

        Refresh();
    }

    /// <summary>フォルダ整理ダイアログを生成して表示する。</summary>
    private void ShowOrganizeDialog(List<(string FilePath, string Reason)> files,
                                    ProjectData project, string rootPath)
    {
        var destinations = new List<(string Path, string Display)>
        {
            (rootPath, "📁 プロジェクトルート")
        };
        foreach (var cat in project.Categories)
        {
            if (Directory.Exists(cat.FolderPath))
                destinations.Add((cat.FolderPath, $"📂 {cat.Name}"));
        }
        foreach (var task in project.Tasks)
        {
            if (Directory.Exists(task.FolderPath))
            {
                var cat = project.Categories.FirstOrDefault(c => c.Id == task.CategoryId);
                var prefix = cat != null ? $"{cat.Name}/" : "";
                destinations.Add((task.FolderPath, $"  ✅ {prefix}{task.Name}"));
            }
        }

        var fg     = new SolidColorBrush(Color.FromRgb(207, 207, 207));
        var bg     = new SolidColorBrush(Color.FromRgb(32,  32,  32));
        var cardBg = new SolidColorBrush(Color.FromRgb(47,  47,  47));
        var dim    = new SolidColorBrush(Color.FromRgb(120, 119, 116));
        var cbBg   = new SolidColorBrush(Color.FromRgb(55,  55,  55));

        var win = new Window
        {
            Title = "フォルダ整理", Width = 720, Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), Background = bg
        };

        var outer = new StackPanel { Margin = new Thickness(20) };
        outer.Children.Add(new TextBlock
        {
            Text = $"整理対象ファイル: {files.Count} 件", FontSize = 14,
            FontWeight = FontWeights.Bold, Foreground = fg, Margin = new Thickness(0, 0, 0, 4)
        });
        outer.Children.Add(new TextBlock
        {
            Text = "移動先を選択して「一括移動」を実行してください",
            FontSize = 12, Foreground = dim, Margin = new Thickness(0, 0, 0, 14)
        });

        var listSp = new StackPanel();
        var destMap = new Dictionary<string, ComboBox>();

        foreach (var (fp, reason) in files)
        {
            var row = new Border
            {
                Background = cardBg, CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

            var leftSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            leftSp.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(fp), FontSize = 13, FontWeight = FontWeights.Bold, Foreground = fg
            });
            leftSp.Children.Add(new TextBlock { Text = reason, FontSize = 11, Foreground = dim });

            var cb = new ComboBox
            {
                Background = cbBg, Foreground = fg,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
            };
            foreach (var (dp, dd) in destinations)
                cb.Items.Add(new { Path = dp, Display = dd });
            cb.DisplayMemberPath = "Display";
            cb.SelectedValuePath = "Path";
            cb.SelectedIndex = 0;
            destMap[fp] = cb;

            Grid.SetColumn(leftSp, 0); Grid.SetColumn(cb, 1);
            g.Children.Add(leftSp); g.Children.Add(cb);
            row.Child = g;
            listSp.Children.Add(row);
        }

        var scroll = new ScrollViewer
        {
            Height = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = listSp
        };
        outer.Children.Add(scroll);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancelBtn = new Button { Content = "キャンセル", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var moveBtn = new Button
        {
            Content = "📦 一括移動", Padding = new Thickness(16, 6, 16, 6),
            Background = new SolidColorBrush(Color.FromRgb(35, 131, 226)),
            Foreground = Brushes.White, FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };

        cancelBtn.Click += (_, _) => win.Close();
        moveBtn.Click += (_, _) =>
        {
            int moved = 0, failed = 0;
            foreach (var (src, _) in files)
            {
                if (!destMap.TryGetValue(src, out var cb2)) continue;
                dynamic? sel = cb2.SelectedItem;
                var destDir  = sel?.Path as string ?? rootPath;
                var srcName  = Path.GetFileName(src);
                var destPath = Path.Combine(destDir, srcName);
                try
                {
                    if (destPath == src) continue;
                    if (File.Exists(destPath) || Directory.Exists(destPath))
                    {
                        var nameOnly = Path.GetFileNameWithoutExtension(src);
                        var ext      = Path.GetExtension(src);
                        destPath = Path.Combine(destDir, $"{nameOnly}_moved{ext}");
                    }
                    bool isDir = Directory.Exists(src);
                    if (isDir) Directory.Move(src, destPath);
                    else       File.Move(src, destPath);
                    moved++;
                }
                catch { failed++; }
            }
            win.Close();
            if (failed > 0)
                AppDialog.ShowWarning($"移動完了: {moved} 件\n失敗: {failed} 件", "フォルダ整理", Window.GetWindow(this));
            else
                AppDialog.ShowInfo($"移動完了: {moved} 件", "フォルダ整理", Window.GetWindow(this));
        };

        btnPanel.Children.Add(cancelBtn); btnPanel.Children.Add(moveBtn);
        outer.Children.Add(btnPanel);
        win.Content = new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        win.ShowDialog();
    }

    private void AddSectionDividerToPanel(StackPanel panel, string title)
    {
        panel.Children.Add(new TextBlock
        {
            Text = title, FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentCyanBrush"),
            Margin = new Thickness(0, 14, 0, 6),
        });
        panel.Children.Add(new Border
        {
            Height = 1, Background = (Brush)FindResource("BorderBrush"),
            Margin = new Thickness(0, 0, 0, 8),
        });
    }

    private void AddInfoRowToPanel(StackPanel panel, string label, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11,
            Foreground = (Brush)FindResource("TextDimBrush"),
        });
        row.Children.Add(new TextBlock
        {
            Text = value, FontSize = 12,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(row);
    }

}

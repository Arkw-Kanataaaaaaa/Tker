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
        public string LastOpenedLabel    { get; init; } = "";
    }

    private readonly MainViewModel _vm;
    private string? _selectedPath;
    private string _coverImageData = string.Empty;
    private bool _isFolderManagementEnabled = true;
    private bool _isFolderMgmtToggleAnimating = false;
    private readonly SolidColorBrush _folderMgmtToggleBg = new(Color.FromRgb(35, 131, 226));
    private bool _isGridMode = true;
    private readonly Dictionary<string, BitmapImage?> _coverBitmapCache = new();

    /// <summary>コンストラクタ。ViewModelを受け取り初期化する。</summary>
    public ProjectListPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        FolderMgmtToggleSwitch.Background = _folderMgmtToggleBg;

        Loaded += (_, _) =>
        {
            UpdateDisplayModeButtons();
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

        var summaries = _vm.AppSettingsService.CollectSummaries()
            .OrderByDescending(s => s.Entry.LastOpened)
            .ToList();
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
                LastOpenedLabel = s.Entry.LastOpened.ToString("yyyy/MM/dd"),
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

    // ── グリッド/リスト表示モード切替 ──────────────────────
    /// <summary>グリッド表示に切り替える。</summary>
    private void SetGridView_Click(object sender, MouseButtonEventArgs e)
    {
        _isGridMode = true;
        ProjectGridControl.Visibility = Visibility.Visible;
        ProjectListSection.Visibility = Visibility.Collapsed;
        UpdateDisplayModeButtons();
    }

    /// <summary>リスト表示に切り替える。</summary>
    private void SetListView_Click(object sender, MouseButtonEventArgs e)
    {
        _isGridMode = false;
        ProjectGridControl.Visibility = Visibility.Collapsed;
        ProjectListSection.Visibility = Visibility.Visible;
        UpdateDisplayModeButtons();
    }

    /// <summary>グリッド/リスト切替ボタンの背景色を現在のモードに合わせて更新する。</summary>
    private void UpdateDisplayModeButtons()
    {
        var activeBg   = (Brush)FindResource("BgCardBrush");
        var inactiveBg = (Brush)FindResource("BgSecondaryBrush");
        BtnViewGrid.Background = _isGridMode ? activeBg : inactiveBg;
        BtnViewList.Background = _isGridMode ? inactiveBg : activeBg;
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
        ProjectDetailOverlay.Visibility = Visibility.Collapsed;
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

                ProjectInfoContent.Children.Add(new Border
                {
                    Height        = 180,
                    CornerRadius  = new CornerRadius(8),
                    ClipToBounds  = true,
                    Margin        = new Thickness(0, 0, 0, 16),
                    Child         = new Image { Source = bmp, Stretch = Stretch.UniformToFill }
                });
            }
            catch { /* 画像デコード失敗は無視 */ }
        }

        // プロジェクト名
        ProjectInfoContent.Children.Add(new TextBlock
        {
            Text = entry.ProjectName,
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = FontWeights.Bold, FontSize = 20,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // 期間
        if (projectData?.Settings.ProjectStartDate != null || projectData?.Settings.ProjectEndDate != null)
        {
            var start = projectData?.Settings.ProjectStartDate?.ToString("yyyy/MM/dd") ?? "─";
            var end   = projectData?.Settings.ProjectEndDate?.ToString("yyyy/MM/dd")   ?? "─";
            ProjectInfoContent.Children.Add(new TextBlock
            {
                Text       = $"📅 {start}  〜  {end}",
                FontSize   = 12,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin     = new Thickness(0, 0, 0, 10)
            });
        }

        // 説明
        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            ProjectInfoContent.Children.Add(new TextBlock
            {
                Text = entry.Description,
                FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 14)
            });
        }

        // パス
        AddInfoRowToPanel(ProjectInfoContent, "パス", entry.ProjectPath ?? "");
        AddInfoRowToPanel(ProjectInfoContent, "ファイル", entry.DataFilePath ?? "");
        AddInfoRowToPanel(ProjectInfoContent, "最終オープン", entry.LastOpened.ToString("yyyy/MM/dd HH:mm"));

        // タスク統計
        if (projectData != null)
        {
            var tasks   = projectData.Tasks;
            int total   = tasks.Count;
            int done    = tasks.Count(t => t.Status == "完了");
            int wip     = tasks.Count(t => t.Status == "進行中");
            int todo    = tasks.Count(t => t.Status == "未着手");
            int overdue = tasks.Count(t => t.IsOverdue);

            AddSectionDividerToPanel(ProjectInfoContent, "タスク統計");
            AddInfoRowToPanel(ProjectInfoContent, "合計",   $"{total} 件");
            AddInfoRowToPanel(ProjectInfoContent, "完了",   $"{done} 件");
            AddInfoRowToPanel(ProjectInfoContent, "進行中", $"{wip} 件");
            AddInfoRowToPanel(ProjectInfoContent, "未着手", $"{todo} 件");
            if (overdue > 0)
                AddInfoRowToPanel(ProjectInfoContent, "期限超過", $"⚠ {overdue} 件");
        }

        // ボタン群
        AddSectionDividerToPanel(ProjectInfoContent, "操作");
        var btnPanel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

        var btnActive = new Button
        {
            Content = "アクティブに設定",
            Style = (Style)FindResource("PrimaryButton"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 8)
        };
        btnActive.Click += (_, _) => SetActive_Click(btnActive, new RoutedEventArgs());
        btnPanel.Children.Add(btnActive);

        var btnExplorer = new Button
        {
            Content = "エクスプローラー",
            Style = (Style)FindResource("SecondaryButton"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 8)
        };
        btnExplorer.Click += (_, _) => OpenFolder_Click(btnExplorer, new RoutedEventArgs());
        btnPanel.Children.Add(btnExplorer);

        var btnEdit = new Button
        {
            Content = "編集",
            Style = (Style)FindResource("SecondaryButton"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 8)
        };
        btnEdit.Click += (_, _) => EditSettings_Click(btnEdit, new RoutedEventArgs());
        btnPanel.Children.Add(btnEdit);

        var btnDelete = new Button
        {
            Content = "削除",
            Style = (Style)FindResource("DangerButton"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 0, 8)
        };
        btnDelete.Click += (_, _) => RemoveSelected_Click(btnDelete, new RoutedEventArgs());
        btnPanel.Children.Add(btnDelete);

        ProjectInfoContent.Children.Add(btnPanel);

        OpenDetailDrawer();
    }


    /// <summary>プロジェクト詳細ドロワーをスライドインで表示する。既に表示中の場合は何もしない。</summary>
    private void OpenDetailDrawer()
    {
        if (ProjectDetailOverlay.Visibility == Visibility.Visible) return;
        ProjectDetailOverlay.Visibility = Visibility.Visible;
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 520, To = 0,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        DetailPanelTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
    }

    /// <summary>プロジェクト詳細ドロワーのクローズボタンのハンドラ。</summary>
    private void CloseDetailPanel_Click(object sender, RoutedEventArgs e) => CloseDetailDrawer();

    /// <summary>プロジェクト詳細ドロワーをスライドアウトして閉じる。</summary>
    private void CloseDetailDrawer()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0, To = 520,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
            }
        };
        anim.Completed += (_, _) =>
        {
            ProjectDetailOverlay.Visibility = Visibility.Collapsed;
            _selectedPath = null;
            ApplyFilter();
        };
        DetailPanelTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
    }

    /// <summary>新規プロジェクト作成フォームをドロワーとしてスライドイン表示する。</summary>
    private void ShowAddProjectPanel()
    {
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

        // オーバーレイを表示してスライドイン
        AddProjectOverlay.Visibility = Visibility.Visible;
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From           = 500,
            To             = 0,
            Duration       = TimeSpan.FromMilliseconds(260),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        AddPanelTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);

        TxtNewProjName.Focus();
    }

    /// <summary>新規プロジェクト作成ドロワーをスライドアウトして閉じる。</summary>
    private void HideAddProjectPanel()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From           = 0,
            To             = 500,
            Duration       = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
            }
        };
        anim.Completed += (_, _) => AddProjectOverlay.Visibility = Visibility.Collapsed;
        AddPanelTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
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
            if (e.Key == Key.F2)
            {
                EditSettings_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchSection.Visibility == Visibility.Visible)
            {
                ToggleSearch_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && AddProjectOverlay.Visibility == Visibility.Visible)
            {
                CancelAddProject_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && ProjectDetailOverlay.Visibility == Visibility.Visible)
            {
                CloseDetailDrawer();
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
    /// <summary>プロジェクトカードのクリックで選択・非選択を切り替える。</summary>
    private void ProjectCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;

        FrameworkElement fe = (FrameworkElement)sender;
        if (fe.DataContext is not ProjectCardItem data) return;
        string path = data.Entry.DataFilePath ?? "";

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

    /// <summary>表紙画像ファイルを選択して読み込みプレビュー表示する。</summary>
    private void BrowseCoverImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "表紙画像を選択",
            Filter = "画像ファイル|*.jpg;*.jpeg;*.png;*.bmp;*.gif|すべてのファイル|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            _coverImageData = Convert.ToBase64String(bytes);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource  = new MemoryStream(bytes);
            bmp.CacheOption   = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            CoverImagePreview.Source         = bmp;
            CoverImagePreview.Visibility     = Visibility.Visible;
            CoverImagePlaceholder.Visibility = Visibility.Collapsed;
            BtnClearCoverImage.Visibility    = Visibility.Visible;
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"画像の読み込みに失敗しました:\n{ex.Message}", "エラー", Window.GetWindow(this));
        }
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

    /// <summary>プロジェクト設定編集ダイアログを表示して設定を保存する。</summary>
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
        {
            project = _vm.ProjectService.CurrentProject;
        }
        else
        {
            try
            {
                var json = File.ReadAllText(path);
                project = JsonConvert.DeserializeObject<ProjectData>(json);
            }
            catch { project = null; }
        }

        if (project == null)
        { AppDialog.ShowError("プロジェクトを開けませんでした", "エラー", Window.GetWindow(this)); return; }

        var dlg = new TKer.Views.Dialogs.ProjectSettingsDialog(project)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() == true)
        {
            project.Settings.ProjectName = dlg.ProjectName;
            project.Settings.Description = dlg.Description;
            project.ProjectVersion       = dlg.Version;
            project.Manager              = dlg.Manager;

            if (path == _vm.ProjectService.ProjectFilePath)
            {
                _vm.ProjectService.SaveProject();
            }
            else
            {
                try
                {
                    var json = JsonConvert.SerializeObject(project, Formatting.Indented);
                    File.WriteAllText(path, json);
                    _vm.AppSettingsService.RegisterProject(path, dlg.ProjectName,
                        project.Settings.ProjectPath, dlg.ProjectName);
                    _vm.AppSettingsService.CollectSummaries(forceRefresh: true);
                }
                catch { }
            }
            Refresh();
        }
    }

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

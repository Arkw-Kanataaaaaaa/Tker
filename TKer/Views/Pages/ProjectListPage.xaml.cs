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
    }

    private readonly MainViewModel _vm;
    private string? _selectedPath;

    // ── ProjectPage から移植: ツリー/列カスタマイズ状態 ──
    private FileNode? _selectedNode;
    private static readonly string[] ALL_COLUMNS = { "名前", "更新日時", "種類", "サイズ" };
    private List<string> _columnOrder = new() { "名前", "更新日時", "種類", "サイズ" };
    private readonly HashSet<string> _hiddenColumns = new();

    /// <summary>コンストラクタ。ViewModelを受け取り初期化する。</summary>
    public ProjectListPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

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

    private Window? _keyDownWindow;

    // ── IRefreshable ──────────────────────────────────────
    /// <summary>ページ全体のUIを最新データで再描画する。</summary>
    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("PL_Header"));
        ApplyFilter();
        ApplyBackground();

        // TreePanel 表示中なら再読み込み
        if (TreePanel.Visibility == Visibility.Visible)
            ShowTreePanel();
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
            NoProjectBanner.Visibility      = Visibility.Visible;
            ProjectItemsControl.ItemsSource = null;
        }
        else
        {
            NoProjectBanner.Visibility      = Visibility.Collapsed;
            ProjectItemsControl.ItemsSource = summaries.Select(s => new ProjectCardItem
            {
                Entry         = s.Entry,
                TotalTasks    = s.TotalTasks,
                DoneTasks     = s.DoneTasks,
                WipTasks      = s.WipTasks,
                OverdueTasks  = s.OverdueTasks,
                ProgressRate  = s.ProgressRate,
                ProgressLabel = s.ProgressLabel,
                HasAlert      = s.HasAlert,
                CreatedAt     = s.CreatedAt,
                IsActive      = s.Entry.DataFilePath == activeFilePath,
                IsSelected    = s.Entry.DataFilePath == _selectedPath
            }).ToList();
        }
    }

    // ── 右パネル制御 ─────────────────────────────────────
    /// <summary>右パネルを空（未選択）状態に切り替える。</summary>
    private void ShowEmptyPanel()
    {
        EmptyPanel.Visibility       = Visibility.Visible;
        ProjectInfoPanel.Visibility = Visibility.Collapsed;
        TreePanel.Visibility        = Visibility.Collapsed;
        HideFileToolbar();
    }

    /// <summary>右パネルにプロジェクト情報を表示する。</summary>
    private void ShowInfoPanel()
    {
        EmptyPanel.Visibility       = Visibility.Collapsed;
        TreePanel.Visibility        = Visibility.Collapsed;
        ProjectInfoPanel.Visibility = Visibility.Visible;
        HideFileToolbar();

        ProjectInfoContent.Children.Clear();

        var entry = _vm.AppSettingsService.RecentProjects.FirstOrDefault(p => p.DataFilePath == _selectedPath);
        if (entry == null) return;

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
        try
        {
            if (File.Exists(_selectedPath))
            {
                var json = File.ReadAllText(_selectedPath!);
                var project = JsonConvert.DeserializeObject<ProjectData>(json);
                if (project != null)
                {
                    var tasks = project.Tasks;
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
            }
        }
        catch { /* 読み込み失敗は無視 */ }

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
    }

    /// <summary>右パネルにフォルダツリーを表示する。</summary>
    private void ShowTreePanel()
    {
        EmptyPanel.Visibility       = Visibility.Collapsed;
        ProjectInfoPanel.Visibility = Visibility.Collapsed;
        TreePanel.Visibility        = Visibility.Visible;
        ShowFileToolbar();

        var tree = _vm.ProjectService.GetProjectFolderTree();
        if (tree != null)
            FolderTree.ItemsSource = new[] { tree };
        UpdateFileToolbarState();
    }

    /// <summary>ファイル操作ツールバーを表示する。</summary>
    private void ShowFileToolbar()  => BtnTreeSection.Visibility = Visibility.Visible;
    /// <summary>ファイル操作ツールバーを非表示にする。</summary>
    private void HideFileToolbar()  => BtnTreeSection.Visibility = Visibility.Collapsed;

    /// <summary>プロジェクトツールバーボタンの有効/無効を更新する。</summary>
    private void UpdateProjectToolbarState()
    {
        bool hasSelection = !string.IsNullOrEmpty(_selectedPath);
        BtnSetActive.IsEnabled      = hasSelection;
        BtnOpenFolder.IsEnabled     = hasSelection;
        BtnEditSettings.IsEnabled   = hasSelection;
        BtnRemoveSelected.IsEnabled = hasSelection;
    }

    /// <summary>ファイル操作ツールバーボタンの有効/無効を更新する。</summary>
    private void UpdateFileToolbarState()
    {
        bool hasNode = _selectedNode != null;
        bool isDir   = _selectedNode?.IsDirectory == true;

        BtnOpen.IsEnabled      = hasNode;
        BtnNewFolder.IsEnabled = isDir;
        BtnNewFile.IsEnabled   = isDir;
        BtnRename.IsEnabled    = hasNode && !IsProtectedPath(_selectedNode!.FullPath, allowRenameRoot: false);
        BtnCopyPath.IsEnabled  = hasNode;
        BtnDelete.IsEnabled    = hasNode && !IsProtectedPath(_selectedNode!.FullPath, allowRenameRoot: false);
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
        if (((Border)sender).DataContext is not ProjectCardItem data) return;
        string path = data.Entry.DataFilePath ?? "";
        _selectedPath = _selectedPath == path ? null : path;
        ApplyFilter();

        if (string.IsNullOrEmpty(_selectedPath))
        {
            ShowEmptyPanel();
            return;
        }

        bool isActive = (_selectedPath == _vm.ProjectService.ProjectFilePath);
        if (isActive)
            ShowTreePanel();
        else
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
        ShowTreePanel();
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

    /// <summary>新規プロジェクト作成ダイアログを表示してプロジェクトを作成する。</summary>
    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dlg = new NewProjectDialog { Owner = owner };
        if (dlg.ShowDialog() != true) return;

        _vm.ProjectService.CreateProject(dlg.SavePath, dlg.ProjectName, dlg.Description);

        var customPresets = _vm.AppSettingsService.Settings.CategoryPresets;
        var templateDlg = new CategoryTemplateDialog(customPresets) { Owner = owner };
        if (templateDlg.ShowDialog() == true)
        {
            foreach (var item in templateDlg.SelectedCategories)
                _vm.ProjectService.AddCategory(item.Name, item.Description, item.Color);
        }
        Refresh();
    }

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

    // ── ピン留め ──────────────────────────────────────────
    /// <summary>プロジェクトのピン留めをトグルする。</summary>
    private void PinProject_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is string path)
        {
            _vm.AppSettingsService.TogglePin(path);
            Refresh();
        }
    }

    // ── FolderTree: 選択変更 (ProjectPage から移植) ───────
    /// <summary>フォルダツリーの選択変更時に詳細パネルを更新する。</summary>
    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FileNode node) { _selectedNode = null; UpdateFileToolbarState(); return; }
        _selectedNode = node;
        UpdateFileToolbarState();

        DetailPanel.Children.Clear();

        DetailPanel.Children.Add(new TextBlock
        {
            Text         = node.FullPath,
            FontFamily   = new FontFamily("Consolas"),
            FontSize     = 11,
            Foreground   = (Brush)FindResource("TextDimBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 10)
        });

        if (node.IsDirectory)
        {
            var cat = FindCategoryByPath(node.FullPath);
            if (cat != null) { ShowCategoryDetail(cat, node); return; }

            var (task, taskCat) = FindTaskByPath(node.FullPath);
            if (task != null) { ShowTaskDetail(task, taskCat, node); return; }

            ShowGenericFolderDetail(node);
        }
        else
        {
            ShowFileDetail(node);
        }
    }

    // ── CAT フォルダ: カテゴリー情報 ─────────────────────
    /// <summary>カテゴリーフォルダ選択時に詳細パネルにカテゴリー情報を表示する。</summary>
    private void ShowCategoryDetail(Category cat, FileNode node)
    {
        var project = _vm.ProjectService.CurrentProject!;

        var tagSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        tagSp.Children.Add(UiBadgeHelper.MakeBadge("📂 カテゴリー", "#3D7EFF"));
        DetailPanel.Children.Add(tagSp);

        var headerSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        headerSp.Children.Add(new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(7),
            Background = UiBadgeHelper.ParseBrush(cat.Color),
            Margin = new Thickness(0, 3, 10, 0), VerticalAlignment = VerticalAlignment.Top
        });
        headerSp.Children.Add(new TextBlock
        {
            Text = cat.Name, FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = FontWeights.Bold, FontSize = 20,
            Foreground = (Brush)FindResource("TextPrimaryBrush"), TextWrapping = TextWrapping.Wrap
        });
        DetailPanel.Children.Add(headerSp);

        if (!string.IsNullOrWhiteSpace(cat.Description))
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = cat.Description, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 14)
            });
        }

        var tasks = project.Tasks.Where(t => t.CategoryId == cat.Id).ToList();
        int total   = tasks.Count;
        int done    = tasks.Count(t => t.Status == "完了");
        int wip     = tasks.Count(t => t.Status == "進行中");
        int todo    = tasks.Count(t => t.Status == "未着手");
        int overdue = tasks.Count(t => t.IsOverdue);

        AddInfoRow("作成日時",       cat.CreatedAt.ToString("yyyy/MM/dd"));
        AddInfoRow("タスク数",       $"{total} 件（完了 {done} / 進行中 {wip} / 未着手 {todo}）");
        if (overdue > 0)
            AddInfoRow("期限超過",   $"⚠ {overdue} 件");

        if (tasks.Count > 0)
        {
            AddSectionDivider("タスク一覧");
            foreach (var t in tasks.OrderBy(t => t.PlannedEndDate ?? DateTime.MaxValue))
                DetailPanel.Children.Add(BuildTaskRow(t));
        }

        AddChildFileSection(node);
    }

    // ── TSK フォルダ: タスク情報 ──────────────────────────
    /// <summary>タスクフォルダ選択時に詳細パネルにタスク情報を表示する。</summary>
    private void ShowTaskDetail(TaskItem task, Category? cat, FileNode node)
    {
        var badgeSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        badgeSp.Children.Add(UiBadgeHelper.MakeBadge("✅ タスク", "#2E7D32"));
        badgeSp.Children.Add(UiBadgeHelper.MakeBadge(task.Status, UiBadgeHelper.StatusColor(task.Status), margin: 6));
        badgeSp.Children.Add(UiBadgeHelper.MakeBadge($"優先度: {task.Priority}", UiBadgeHelper.PriorityColor(task.Priority), margin: 6));
        if (task.IsOverdue)
            badgeSp.Children.Add(UiBadgeHelper.MakeBadge("⚠ 期限超過", "#C62828", margin: 6));
        else if (task.IsDueSoon)
            badgeSp.Children.Add(UiBadgeHelper.MakeBadge("⏰ 期限間近", "#E65100", margin: 6));
        DetailPanel.Children.Add(badgeSp);

        DetailPanel.Children.Add(new TextBlock
        {
            Text = task.Name, FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = FontWeights.Bold, FontSize = 20,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });

        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = task.Description, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 14)
            });
        }

        if (cat != null)
            AddInfoRow("カテゴリー", cat.Name);
        if (!string.IsNullOrWhiteSpace(task.Assignee))
            AddInfoRow("担当者", task.Assignee);
        if (!string.IsNullOrWhiteSpace(task.SubCategory))
            AddInfoRow("サブカテゴリー", task.SubCategory);
        if (!string.IsNullOrWhiteSpace(task.Environment))
            AddInfoRow("環境", task.Environment);

        AddSectionDivider("日程");
        AddInfoRow("予定開始", task.PlannedStartDate?.ToString("yyyy/MM/dd") ?? "─");
        AddInfoRow("予定終了", task.PlannedEndDate?.ToString("yyyy/MM/dd")  ?? "─");
        AddInfoRow("実績開始", task.ActualStartDate?.ToString("yyyy/MM/dd") ?? "─");
        AddInfoRow("実績終了", task.ActualEndDate?.ToString("yyyy/MM/dd")   ?? "─");

        if (task.RemainingDays.HasValue && task.Status != "完了")
        {
            var rd = task.RemainingDays.Value;
            AddInfoRow("残り日数", rd >= 0 ? $"{rd} 日" : $"超過 {-rd} 日");
        }
        if (task.DelayDays.HasValue && task.DelayDays.Value != 0)
            AddInfoRow("遅延日数", $"{task.DelayDays.Value} 日");

        if (!string.IsNullOrWhiteSpace(task.Tags))
        {
            AddSectionDivider("タグ");
            var tagWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var tag in task.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                tagWrap.Children.Add(UiBadgeHelper.MakeBadge(tag, "#37474F", margin: 4));
            DetailPanel.Children.Add(tagWrap);
        }
        if (!string.IsNullOrWhiteSpace(task.Notes))
        {
            AddSectionDivider("メモ");
            DetailPanel.Children.Add(new TextBlock
            {
                Text = task.Notes, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        AddProgressTagSection(task);
        AddChildFileSection(node, task);
    }

    // ── 汎用フォルダ ──────────────────────────────────────
    /// <summary>汎用フォルダ選択時に詳細パネルにフォルダ情報を表示する。</summary>
    private void ShowGenericFolderDetail(FileNode node)
    {
        DetailPanel.Children.Add(new TextBlock
        {
            Text = $"📁  {node.Name}", FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = FontWeights.Bold, FontSize = 20,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        if (Directory.Exists(node.FullPath))
        {
            var di = new DirectoryInfo(node.FullPath);
            AddInfoRow("種類",     "ファイル フォルダー");
            AddInfoRow("更新日時", di.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"));
            AddInfoRow("内容",     $"{node.Children.Count} 件");
        }

        AddChildFileSection(node);
    }

    // ── ファイル詳細 ──────────────────────────────────────
    /// <summary>ファイル選択時に詳細パネルにファイル情報を表示する。</summary>
    private void ShowFileDetail(FileNode node)
    {
        DetailPanel.Children.Add(new TextBlock
        {
            Text = $"{node.Icon}  {node.Name}", FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = FontWeights.Bold, FontSize = 20,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        if (File.Exists(node.FullPath))
        {
            var fi = new FileInfo(node.FullPath);
            AddInfoRow("種類",     FileHelper.GetFileType(node.FullPath));
            AddInfoRow("サイズ",   FileHelper.FormatSize(fi.Length));
            AddInfoRow("更新日時", fi.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"));
            AddInfoRow("作成日時", fi.CreationTime.ToString("yyyy/MM/dd HH:mm:ss"));

            var openBtn = new Button
            {
                Content = "📂 ファイルを開く",
                Style = (Style)FindResource("SecondaryButton"),
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            openBtn.Click += (_, _) =>
                Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true });
            DetailPanel.Children.Add(openBtn);
        }
    }

    // ── 子ファイル一覧セクション（共通） ─────────────────
    /// <summary>詳細パネルにノードの子ファイル一覧セクションを追加する。</summary>
    private void AddChildFileSection(FileNode node, TaskItem? ownerTask = null)
    {
        if (node.Children.Count == 0) return;

        var sectionSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 6) };
        sectionSp.Children.Add(new TextBlock
        {
            Text = "フォルダ内容", FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        });
        sectionSp.Children.Add(new Border
        {
            Height = 1, Background = (Brush)FindResource("BorderBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 60
        });
        DetailPanel.Children.Add(sectionSp);

        DetailPanel.Children.Add(BuildChildHeader());

        foreach (var child in node.Children
                     .OrderByDescending(c => c.IsDirectory)
                     .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            DetailPanel.Children.Add(BuildChildRow(child, ownerTask));
        }
    }

    // ── タスク行（カテゴリー詳細内） ──────────────────────
    /// <summary>カテゴリー詳細内のタスク行UIを生成する。</summary>
    private Border BuildTaskRow(TaskItem task)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var nameTb = new TextBlock
        {
            Text = task.Name, FontSize = 12,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0)
        };
        var statusBadge = UiBadgeHelper.MakeBadge(task.Status, UiBadgeHelper.StatusColor(task.Status));
        var dateTb = new TextBlock
        {
            Text = task.PlannedEndDate?.ToString("MM/dd") ?? "─",
            FontSize = 11, Foreground = task.IsOverdue
                ? new SolidColorBrush(Color.FromRgb(239, 83, 80))
                : (Brush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        Grid.SetColumn(nameTb, 0);
        Grid.SetColumn(statusBadge, 1);
        Grid.SetColumn(dateTb, 2);
        g.Children.Add(nameTb); g.Children.Add(statusBadge); g.Children.Add(dateTb);

        return new Border
        {
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 6, 0, 6),
            Child = g
        };
    }

    // ── マッチング ────────────────────────────────────────
    /// <summary>フルパスに一致するカテゴリーを返す。</summary>
    private Category? FindCategoryByPath(string fullPath)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) return null;
        return project.Categories.FirstOrDefault(c =>
            !string.IsNullOrEmpty(c.FolderPath) &&
            string.Equals(Path.GetFullPath(c.FolderPath),
                          Path.GetFullPath(fullPath),
                          StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>フルパスに一致するタスクとそのカテゴリーを返す。</summary>
    private (TaskItem? task, Category? cat) FindTaskByPath(string fullPath)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) return (null, null);
        var task = project.Tasks.FirstOrDefault(t =>
            !string.IsNullOrEmpty(t.FolderPath) &&
            string.Equals(Path.GetFullPath(t.FolderPath),
                          Path.GetFullPath(fullPath),
                          StringComparison.OrdinalIgnoreCase));
        if (task == null) return (null, null);
        var cat = project.Categories.FirstOrDefault(c => c.Id == task.CategoryId);
        return (task, cat);
    }

    // ── UI ヘルパー (DetailPanel 用) ─────────────────────
    /// <summary>詳細パネルにセクション区切り線とタイトルを追加する。</summary>
    private void AddSectionDivider(string title)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 6) };
        sp.Children.Add(new TextBlock
        {
            Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        });
        sp.Children.Add(new Border
        {
            Height = 1, Background = (Brush)FindResource("BorderBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 60
        });
        DetailPanel.Children.Add(sp);
    }

    /// <summary>詳細パネルにラベルと値の情報行を追加する。</summary>
    private void AddInfoRow(string label, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock
        {
            Text       = label,
            FontSize   = 12,
            Foreground = (Brush)FindResource("TextDimBrush")
        };
        var v = new TextBlock
        {
            Text       = value,
            FontSize   = 12,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(l, 0); Grid.SetColumn(v, 1);
        g.Children.Add(l); g.Children.Add(v);
        DetailPanel.Children.Add(g);
    }

    // ── UI ヘルパー (任意パネル用) ────────────────────────
    /// <summary>指定パネルにセクション区切り線とタイトルを追加する。</summary>
    private void AddSectionDividerToPanel(StackPanel panel, string title)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 6) };
        sp.Children.Add(new TextBlock
        {
            Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        });
        sp.Children.Add(new Border
        {
            Height = 1, Background = (Brush)FindResource("BorderBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 60
        });
        panel.Children.Add(sp);
    }

    /// <summary>指定パネルにラベルと値の情報行を追加する。</summary>
    private void AddInfoRowToPanel(StackPanel panel, string label, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock
        {
            Text       = label,
            FontSize   = 12,
            Foreground = (Brush)FindResource("TextDimBrush")
        };
        var v = new TextBlock
        {
            Text       = value,
            FontSize   = 12,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(l, 0); Grid.SetColumn(v, 1);
        g.Children.Add(l); g.Children.Add(v);
        panel.Children.Add(g);
    }

    // ── 列カスタマイズ ────────────────────────────────────
    /// <summary>非表示列を除いた表示中の列リストを返す。</summary>
    private List<string> VisibleColumns =>
        _columnOrder.Where(c => !_hiddenColumns.Contains(c)).ToList();

    /// <summary>子ファイル一覧のヘッダー行を生成する。</summary>
    private Border BuildChildHeader()
    {
        var vis = VisibleColumns;
        var g   = MakeRowGrid(vis);
        for (int i = 0; i < vis.Count; i++)
        {
            var align = vis[i] == "サイズ" ? TextAlignment.Right : TextAlignment.Left;
            AddCell(g, vis[i], i, isHeader: true, align: align);
        }

        return new Border
        {
            Background      = (Brush)FindResource("BgSecondaryBrush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(4, 5, 4, 5),
            Child           = g
        };
    }

    /// <summary>子ファイル一覧の1行UIを生成する。</summary>
    private Border BuildChildRow(FileNode child, TaskItem? ownerTask = null)
    {
        string modified, type, size;
        if (child.IsDirectory && Directory.Exists(child.FullPath))
        {
            var di = new DirectoryInfo(child.FullPath);
            modified = di.LastWriteTime.ToString("yyyy/MM/dd HH:mm");
            type     = "ファイル フォルダー";
            size     = "";
        }
        else if (!child.IsDirectory && File.Exists(child.FullPath))
        {
            var fi = new FileInfo(child.FullPath);
            modified = fi.LastWriteTime.ToString("yyyy/MM/dd HH:mm");
            type     = FileHelper.GetFileType(child.FullPath);
            size     = FileHelper.FormatSize(fi.Length);
        }
        else { modified = type = size = "--"; }

        var dataMap = new Dictionary<string, string>
        {
            { "更新日時", modified }, { "種類", type }, { "サイズ", size }
        };

        var vis = VisibleColumns;
        var g   = MakeRowGrid(vis);

        for (int i = 0; i < vis.Count; i++)
        {
            if (vis[i] == "名前")
            {
                var isTagged = ownerTask != null && !child.IsDirectory &&
                    ownerTask.ProgressTagFiles.Any(f =>
                        string.Equals(f, child.FullPath, StringComparison.OrdinalIgnoreCase));

                var nameSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (isTagged)
                {
                    nameSp.Children.Add(new TextBlock
                    {
                        Text = "📌", FontSize = 11, Margin = new Thickness(0, 0, 3, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "進捗反映タグ付き"
                    });
                }
                nameSp.Children.Add(new TextBlock
                {
                    Text = child.Icon, Margin = new Thickness(0, 0, 6, 0),
                    FontSize = 13, VerticalAlignment = VerticalAlignment.Center
                });
                nameSp.Children.Add(new TextBlock
                {
                    Text = child.Name, FontSize = 12,
                    Foreground = isTagged
                        ? new SolidColorBrush(Color.FromRgb(0, 191, 216))
                        : (Brush)FindResource("TextPrimaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });
                Grid.SetColumn(nameSp, i);
                g.Children.Add(nameSp);
            }
            else
            {
                var align = vis[i] == "サイズ" ? TextAlignment.Right : TextAlignment.Left;
                AddCell(g, dataMap.GetValueOrDefault(vis[i], ""), i, isHeader: false, align: align);
            }
        }

        var border = new Border
        {
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(4, 6, 4, 6),
            Cursor          = Cursors.Hand,
            Child           = g
        };

        if (ownerTask != null && !child.IsDirectory)
        {
            var cm = new ContextMenu();
            var task    = ownerTask;
            var fp      = child.FullPath;
            bool tagged = task.ProgressTagFiles.Any(f =>
                string.Equals(f, fp, StringComparison.OrdinalIgnoreCase));

            var tagItem = new MenuItem { Header = tagged ? "📌 タグを解除" : "📌 進捗タグを付ける" };
            tagItem.Click += (_, _) => ToggleProgressTag(task, fp);
            cm.Items.Add(tagItem);
            border.ContextMenu = cm;
        }

        border.MouseEnter        += (_, _) => border.Background = (Brush)FindResource("BgSecondaryBrush");
        border.MouseLeave        += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) =>
        {
            if (child.IsDirectory) ShellHelper.OpenInExplorer(child.FullPath);
            else Process.Start(new ProcessStartInfo(child.FullPath) { UseShellExecute = true });
        };
        return border;
    }

    /// <summary>ファイルの進捗タグを切り替えて詳細パネルを再描画する。</summary>
    private void ToggleProgressTag(TaskItem task, string filePath)
    {
        var existing = task.ProgressTagFiles
            .FirstOrDefault(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            task.ProgressTagFiles.Remove(existing);
        else
            task.ProgressTagFiles.Add(filePath);

        _vm.ProjectService.MarkDirtyAndSave();
        if (_selectedNode != null)
            FolderTree_SelectedItemChanged(FolderTree, new RoutedPropertyChangedEventArgs<object>(null!, _selectedNode));
    }

    /// <summary>詳細パネルに進捗反映タグセクションを追加する。</summary>
    private void AddProgressTagSection(TaskItem task)
    {
        if (!Directory.Exists(task.FolderPath)) return;

        AddSectionDivider("進捗反映タグ");

        var hint = new TextBlock
        {
            Text = "ファイル行を右クリック → タグを付けると、そのファイル変更時に進捗入力ダイアログが表示されます",
            FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        DetailPanel.Children.Add(hint);

        var tagged = task.ProgressTagFiles.Where(File.Exists).ToList();

        if (tagged.Count == 0)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = "タグ付きファイルなし（全ファイルが監視対象）",
                FontSize = 11, Foreground = (Brush)FindResource("TextDimBrush"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            return;
        }

        foreach (var fp in tagged)
        {
            var capturedFp = fp;
            var row = new Border
            {
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 5, 4, 5)
            };
            var dock = new DockPanel { LastChildFill = true };

            var removeBtn = new Button
            {
                Content = "解除", FontSize = 10,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource("SecondaryButton"),
                Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50))
            };
            removeBtn.Click += (_, _) => ToggleProgressTag(task, capturedFp);
            DockPanel.SetDock(removeBtn, Dock.Right);
            dock.Children.Add(removeBtn);

            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = "📌", Margin = new Thickness(0, 0, 5, 0) });
            sp.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(fp), FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 191, 216)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            dock.Children.Add(sp);
            row.Child = dock;
            DetailPanel.Children.Add(row);
        }
    }

    /// <summary>列カスタマイズダイアログを表示して表示列と順序を設定する。</summary>
    private void BtnColumnConfig_Click(object sender, RoutedEventArgs e)
    {
        var bg  = (Brush)FindResource("BgCardBrush");
        var dim = (Brush)FindResource("TextDimBrush");
        var fg  = (Brush)FindResource("TextPrimaryBrush");

        var win = new Window
        {
            Title = "列の表示設定", Width = 320, Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = bg
        };

        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock
        {
            Text = "表示する列を選択してください", FontSize = 12,
            Foreground = dim, Margin = new Thickness(0, 0, 0, 12)
        });

        var listSp = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        sp.Children.Add(listSp);

        void RebuildRows()
        {
            listSp.Children.Clear();
            for (int idx = 0; idx < _columnOrder.Count; idx++)
            {
                var col     = _columnOrder[idx];
                var captCol = col;
                var captIdx = idx;

                var rowSp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin      = new Thickness(0, 0, 0, 6)
                };

                var cb = new CheckBox
                {
                    Content   = col,
                    IsChecked = !_hiddenColumns.Contains(col),
                    FontSize  = 13,
                    Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 140,
                    IsEnabled = col != "名前"
                };
                cb.Checked   += (_, _) => { _hiddenColumns.Remove(captCol); };
                cb.Unchecked += (_, _) => { _hiddenColumns.Add(captCol); };
                rowSp.Children.Add(cb);

                var upBtn = new Button
                {
                    Content = "↑", Width = 28, Height = 24,
                    Padding = new Thickness(0), Margin = new Thickness(4, 0, 2, 0),
                    IsEnabled = captIdx > 0
                };
                upBtn.Click += (_, _) =>
                {
                    int i = _columnOrder.IndexOf(captCol);
                    if (i > 0)
                    {
                        (_columnOrder[i], _columnOrder[i - 1]) = (_columnOrder[i - 1], _columnOrder[i]);
                        RebuildRows();
                    }
                };
                rowSp.Children.Add(upBtn);

                var downBtn = new Button
                {
                    Content = "↓", Width = 28, Height = 24,
                    Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 0),
                    IsEnabled = captIdx < _columnOrder.Count - 1
                };
                downBtn.Click += (_, _) =>
                {
                    int i = _columnOrder.IndexOf(captCol);
                    if (i < _columnOrder.Count - 1)
                    {
                        (_columnOrder[i], _columnOrder[i + 1]) = (_columnOrder[i + 1], _columnOrder[i]);
                        RebuildRows();
                    }
                };
                rowSp.Children.Add(downBtn);

                listSp.Children.Add(rowSp);
            }
        }

        RebuildRows();

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var btnOk = new Button
        {
            Content = "適用", Padding = new Thickness(20, 6, 20, 6),
            Style = (Style)FindResource("PrimaryButton")
        };
        btnOk.Click += (_, _) => { win.DialogResult = true; };
        var btnCancel = new Button
        {
            Content = "キャンセル", Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("SecondaryButton")
        };
        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnOk);
        sp.Children.Add(btnRow);
        win.Content = sp;

        var backupOrder  = new List<string>(_columnOrder);
        var backupHidden = new HashSet<string>(_hiddenColumns);

        if (win.ShowDialog() == true)
        {
            if (_selectedNode != null)
                FolderTree_SelectedItemChanged(FolderTree,
                    new RoutedPropertyChangedEventArgs<object>(null!, _selectedNode));
        }
        else
        {
            _columnOrder.Clear();
            _columnOrder.AddRange(backupOrder);
            _hiddenColumns.Clear();
            foreach (var h in backupHidden) _hiddenColumns.Add(h);
        }
    }

    /// <summary>可視列リストに基づいてGridを生成する。</summary>
    private Grid MakeRowGrid(List<string> visibleCols)
    {
        var g = new Grid();
        foreach (var col in visibleCols)
        {
            g.ColumnDefinitions.Add(col switch
            {
                "名前"    => new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                "更新日時" => new ColumnDefinition { Width = new GridLength(130) },
                "種類"    => new ColumnDefinition { Width = new GridLength(140) },
                "サイズ"  => new ColumnDefinition { Width = new GridLength(72) },
                _         => new ColumnDefinition { Width = new GridLength(100) }
            });
        }
        return g;
    }

    /// <summary>Gridの指定列にテキストセルを追加する。</summary>
    private void AddCell(Grid g, string text, int col, bool isHeader,
                         TextAlignment align = TextAlignment.Left)
    {
        var dimBrush  = (Brush)FindResource("TextDimBrush");
        var mainBrush = (Brush)FindResource("TextSecondaryBrush");

        var tb = new TextBlock
        {
            Text              = text,
            FontSize          = 12,
            FontWeight        = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground        = isHeader ? dimBrush : mainBrush,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment     = align,
            Margin            = new Thickness(4, 0, 4, 0)
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    // ── コンテキストメニュー表示前フック ─────────────────
    /// <summary>コンテキストメニューの開く前に項目の有効/無効を設定する。</summary>
    private void TreeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu cm) return;
        bool hasNode = _selectedNode != null;
        bool isDir   = _selectedNode?.IsDirectory == true;
        bool isRoot  = _selectedNode != null && IsProtectedPath(_selectedNode.FullPath, allowRenameRoot: false);

        foreach (var item in cm.Items.OfType<MenuItem>())
        {
            switch (item.Tag?.ToString())
            {
                case "CtxNewFolder": item.IsEnabled = isDir; break;
                case "CtxNewFile":   item.IsEnabled = isDir; break;
                case "CtxRename":    item.IsEnabled = hasNode && !isRoot; break;
                case "CtxDelete":    item.IsEnabled = hasNode && !isRoot; break;
                default:             item.IsEnabled = hasNode; break;
            }
        }
    }

    // ── ファイル操作 ──────────────────────────────────────
    /// <summary>選択ノードをエクスプローラーまたは関連アプリで開く。</summary>
    private void FileOp_Open_Click(object sender, RoutedEventArgs e)
    {
        var node = GetTargetNode(sender);
        if (node == null) return;

        if (node.IsDirectory)
            ShellHelper.OpenInExplorer(node.FullPath);
        else
            Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true });
    }

    /// <summary>選択フォルダ内に新規フォルダを作成する。</summary>
    private void FileOp_NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var node = GetTargetNode(sender);
        if (node == null || !node.IsDirectory) return;

        var name = PromptName("新規フォルダ名を入力してください", "新規フォルダ");
        if (string.IsNullOrWhiteSpace(name)) return;

        var newPath = Path.Combine(node.FullPath, name);
        if (Directory.Exists(newPath))
        { AppDialog.ShowWarning("同名のフォルダが既に存在します", "エラー", Window.GetWindow(this)); return; }

        try
        {
            Directory.CreateDirectory(newPath);
            ShowTreePanel();
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"フォルダ作成に失敗しました:\n{ex.Message}", "エラー", Window.GetWindow(this));
        }
    }

    /// <summary>選択フォルダ内に新規ファイルを作成する。</summary>
    private void FileOp_NewFile_Click(object sender, RoutedEventArgs e)
    {
        var node = GetTargetNode(sender);
        if (node == null || !node.IsDirectory) return;

        var name = PromptName("新規ファイル名を入力してください（拡張子を含む）", "新規ファイル.txt");
        if (string.IsNullOrWhiteSpace(name)) return;

        var newPath = Path.Combine(node.FullPath, name);
        if (File.Exists(newPath))
        { AppDialog.ShowWarning("同名のファイルが既に存在します", "エラー", Window.GetWindow(this)); return; }

        try
        {
            File.WriteAllText(newPath, "");
            ShowTreePanel();
            Process.Start(new ProcessStartInfo(newPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"ファイル作成に失敗しました:\n{ex.Message}", "エラー", Window.GetWindow(this));
        }
    }

    /// <summary>選択ノードの名前変更ダイアログを表示して名前を変更する。</summary>
    private void FileOp_Rename_Click(object sender, RoutedEventArgs e)
    {
        var node = GetTargetNode(sender);
        if (node == null) return;
        if (IsProtectedPath(node.FullPath, allowRenameRoot: false))
        { AppDialog.ShowWarning("このファイル/フォルダは変更できません", "保護済み", Window.GetWindow(this)); return; }

        var oldName = Path.GetFileName(node.FullPath);
        var newName = PromptName("新しい名前を入力してください", oldName);
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

        var parent  = Path.GetDirectoryName(node.FullPath)!;
        var newPath = Path.Combine(parent, newName);
        try
        {
            if (node.IsDirectory) Directory.Move(node.FullPath, newPath);
            else                  File.Move(node.FullPath, newPath);

            UpdateModelPath(node.FullPath, newPath);
            ShowTreePanel();
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"名前変更に失敗しました:\n{ex.Message}", "エラー", Window.GetWindow(this));
        }
    }

    /// <summary>選択ノードのフルパスをクリップボードにコピーする。</summary>
    private void FileOp_CopyPath_Click(object sender, RoutedEventArgs e)
    {
        var node = GetTargetNode(sender);
        if (node == null) return;
        try { Clipboard.SetText(node.FullPath); }
        catch { }
    }

    /// <summary>選択ノードを削除する確認ダイアログを表示して削除する。</summary>
    private void FileOp_Delete_Click(object sender, RoutedEventArgs e)
    {
        var node = GetTargetNode(sender);
        if (node == null) return;
        if (IsProtectedPath(node.FullPath, allowRenameRoot: false))
        { AppDialog.ShowWarning("このファイル/フォルダは削除できません", "保護済み", Window.GetWindow(this)); return; }

        var typeName = node.IsDirectory ? "フォルダ" : "ファイル";
        if (!AppDialog.Confirm($"{typeName}「{node.Name}」を削除しますか？\n中のファイルも全て削除されます。",
                               "削除確認", Window.GetWindow(this))) return;

        try
        {
            if (node.IsDirectory)
            {
                Directory.Delete(node.FullPath, recursive: true);
                ClearModelReference(node.FullPath);
            }
            else
            {
                File.Delete(node.FullPath);
            }
            _selectedNode = null;
            ShowTreePanel();
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"削除に失敗しました:\n{ex.Message}", "エラー", Window.GetWindow(this));
        }
    }

    /// <summary>変更・削除できないパスかどうかを返す。</summary>
    private bool IsProtectedPath(string fullPath, bool allowRenameRoot)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) return false;

        if (string.Equals(Path.GetFullPath(fullPath),
                          Path.GetFullPath(project.Settings.ProjectPath),
                          StringComparison.OrdinalIgnoreCase)) return !allowRenameRoot;

        var name = Path.GetFileName(fullPath);
        if (name.EndsWith("project_data.json", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith(".json.bak",         StringComparison.OrdinalIgnoreCase)) return true;
        if (name == "作業完了") return true;

        return false;
    }

    /// <summary>リネーム後にモデル内のパスを新しいパスに更新する。</summary>
    private void UpdateModelPath(string oldPath, string newPath)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) return;

        bool dirty = false;
        var oldFull = Path.GetFullPath(oldPath);

        foreach (var cat in project.Categories)
        {
            if (!string.IsNullOrEmpty(cat.FolderPath) &&
                Path.GetFullPath(cat.FolderPath).StartsWith(oldFull, StringComparison.OrdinalIgnoreCase))
            {
                cat.FolderPath = newPath + cat.FolderPath[oldPath.Length..];
                dirty = true;
            }
        }
        foreach (var task in project.Tasks)
        {
            if (!string.IsNullOrEmpty(task.FolderPath) &&
                Path.GetFullPath(task.FolderPath).StartsWith(oldFull, StringComparison.OrdinalIgnoreCase))
            {
                task.FolderPath = newPath + task.FolderPath[oldPath.Length..];
                dirty = true;
            }
        }
        if (dirty) _vm.ProjectService.MarkDirtyAndSave();
    }

    /// <summary>削除後にモデル内の対象フォルダへの参照を解除する。</summary>
    private void ClearModelReference(string deletedPath)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) return;

        bool dirty = false;
        var delFull = Path.GetFullPath(deletedPath);

        foreach (var cat in project.Categories.Where(c =>
            !string.IsNullOrEmpty(c.FolderPath) &&
            Path.GetFullPath(c.FolderPath).StartsWith(delFull, StringComparison.OrdinalIgnoreCase)))
        {
            cat.FolderPath = "";
            cat.FolderCreated = false;
            dirty = true;
        }
        foreach (var task in project.Tasks.Where(t =>
            !string.IsNullOrEmpty(t.FolderPath) &&
            Path.GetFullPath(t.FolderPath).StartsWith(delFull, StringComparison.OrdinalIgnoreCase)))
        {
            task.FolderPath = "";
            task.FolderCreated = false;
            dirty = true;
        }
        if (dirty) _vm.ProjectService.MarkDirtyAndSave();
    }

    /// <summary>操作対象のノードを返す。</summary>
    private FileNode? GetTargetNode(object senderObj) => _selectedNode;

    /// <summary>名前入力ダイアログを表示して入力値を返す。</summary>
    private string? PromptName(string prompt, string defaultValue)
    {
        var bg  = (Brush)FindResource("BgCardBrush");
        var dim = (Brush)FindResource("TextDimBrush");
        var win = new Window
        {
            Title = "名前の入力", Width = 380, Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = bg
        };
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(new TextBlock { Text = prompt, FontSize = 12, Foreground = dim, Margin = new Thickness(0, 0, 0, 8) });
        var tb = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = defaultValue, Margin = new Thickness(0, 0, 0, 16) };
        sp.Children.Add(tb);
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOk = new Button { Content = "OK", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnCancel = new Button { Content = "キャンセル", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(14, 6, 14, 6) };
        btnOk.Click += (_, _) => win.DialogResult = true;
        btnCancel.Click += (_, _) => win.DialogResult = false;
        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        sp.Children.Add(btnPanel);
        win.Content = sp;
        win.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return win.ShowDialog() == true ? tb.Text.Trim() : null;
    }

    /// <summary>ツリーパネル表示中にツリーを再読み込みする。</summary>
    private void RefreshTree_Click(object sender, RoutedEventArgs e)
    {
        if (TreePanel.Visibility == Visibility.Visible)
            ShowTreePanel();
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
            ShowTreePanel();
        };

        btnPanel.Children.Add(cancelBtn); btnPanel.Children.Add(moveBtn);
        outer.Children.Add(btnPanel);
        win.Content = new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        win.ShowDialog();
    }

    private void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm.ProjectService.CurrentProject?.Settings.ProjectPath;
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            ShellHelper.OpenInExplorer(path);
    }
}

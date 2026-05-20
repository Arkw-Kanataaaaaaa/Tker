using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

public partial class ProjectPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;

    // 現在選択中のノード（ファイル操作に使用）
    private FileNode? _selectedNode;

    // ── 列カスタマイズ状態（ファイル一覧） ────────────────
    private static readonly string[] AllColumns = { "名前", "更新日時", "種類", "サイズ" };
    private List<string> _columnOrder = new() { "名前", "更新日時", "種類", "サイズ" };
    private readonly HashSet<string> _hiddenColumns = new();

    public ProjectPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
    }

    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Proj_Header"));
        var tree = _vm.ProjectService.GetProjectFolderTree();
        if (tree != null)
            FolderTree.ItemsSource = new[] { tree };
        UpdateToolbarState();
    }

    // ── ツールバー有効/無効更新 ───────────────────────────
    private void UpdateToolbarState()
    {
        bool hasNode = _selectedNode != null;
        bool isDir   = _selectedNode?.IsDirectory == true;
        bool isFile  = _selectedNode?.IsDirectory == false;

        BtnOpen.IsEnabled      = hasNode;
        BtnNewFolder.IsEnabled = isDir;   // フォルダ選択時のみ子フォルダ作成可
        BtnNewFile.IsEnabled   = isDir;
        BtnRename.IsEnabled    = hasNode && !IsProtectedPath(_selectedNode!.FullPath, allowRenameRoot: false);
        BtnCopyPath.IsEnabled  = hasNode;
        BtnDelete.IsEnabled    = hasNode && !IsProtectedPath(_selectedNode!.FullPath, allowRenameRoot: false);
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FileNode node) { _selectedNode = null; UpdateToolbarState(); return; }
        _selectedNode = node;
        UpdateToolbarState();

        DetailPanel.Children.Clear();

        // パス（常に表示）
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
            // CAT フォルダ判定
            var cat = FindCategoryByPath(node.FullPath);
            if (cat != null) { ShowCategoryDetail(cat, node); return; }

            // TSK フォルダ判定
            var (task, taskCat) = FindTaskByPath(node.FullPath);
            if (task != null) { ShowTaskDetail(task, taskCat, node); return; }

            // 汎用フォルダ
            ShowGenericFolderDetail(node);
        }
        else
        {
            ShowFileDetail(node);
        }
    }

    // ── CAT フォルダ: カテゴリー情報 ─────────────────
    private void ShowCategoryDetail(Category cat, FileNode node)
    {
        var project = _vm.ProjectService.CurrentProject!;

        // タグバッジ
        var tagSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        tagSp.Children.Add(MakeBadge("📂 カテゴリー", "#3D7EFF"));
        DetailPanel.Children.Add(tagSp);

        // カテゴリー名（色ドット付き）
        var headerSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        headerSp.Children.Add(new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(7),
            Background = ParseBrush(cat.Color),
            Margin = new Thickness(0, 3, 10, 0), VerticalAlignment = VerticalAlignment.Top
        });
        headerSp.Children.Add(new TextBlock
        {
            Text = cat.Name, FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = FontWeights.Bold, FontSize = 20,
            Foreground = (Brush)FindResource("TextPrimaryBrush"), TextWrapping = TextWrapping.Wrap
        });
        DetailPanel.Children.Add(headerSp);

        // 説明
        if (!string.IsNullOrWhiteSpace(cat.Description))
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = cat.Description, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 14)
            });
        }

        // タスク統計
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

        // タスク一覧
        if (tasks.Count > 0)
        {
            AddSectionDivider("タスク一覧");
            foreach (var t in tasks.OrderBy(t => t.PlannedEndDate ?? DateTime.MaxValue))
                DetailPanel.Children.Add(BuildTaskRow(t));
        }

        AddChildFileSection(node);
    }

    // ── TSK フォルダ: タスク情報 ──────────────────────
    private void ShowTaskDetail(TaskItem task, Category? cat, FileNode node)
    {
        // タグバッジ群
        var badgeSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        badgeSp.Children.Add(MakeBadge("✅ タスク", "#2E7D32"));
        badgeSp.Children.Add(MakeBadge(task.Status, StatusColor(task.Status), margin: 6));
        badgeSp.Children.Add(MakeBadge($"優先度: {task.Priority}", PriorityColor(task.Priority), margin: 6));
        if (task.IsOverdue)
            badgeSp.Children.Add(MakeBadge("⚠ 期限超過", "#C62828", margin: 6));
        else if (task.IsDueSoon)
            badgeSp.Children.Add(MakeBadge("⏰ 期限間近", "#E65100", margin: 6));
        DetailPanel.Children.Add(badgeSp);

        // タスク名
        DetailPanel.Children.Add(new TextBlock
        {
            Text = task.Name, FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = FontWeights.Bold, FontSize = 20,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });

        // 説明
        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = task.Description, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 14)
            });
        }

        // 基本情報
        if (cat != null)
            AddInfoRow("カテゴリー", cat.Name);
        if (!string.IsNullOrWhiteSpace(task.Assignee))
            AddInfoRow("担当者", task.Assignee);
        if (!string.IsNullOrWhiteSpace(task.SubCategory))
            AddInfoRow("サブカテゴリー", task.SubCategory);
        if (!string.IsNullOrWhiteSpace(task.Environment))
            AddInfoRow("環境", task.Environment);

        // 日程
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

        // タグ・メモ
        if (!string.IsNullOrWhiteSpace(task.Tags))
        {
            AddSectionDivider("タグ");
            var tagWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var tag in task.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                tagWrap.Children.Add(MakeBadge(tag, "#37474F", margin: 4));
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

        // 進捗反映タグセクション
        AddProgressTagSection(task);

        AddChildFileSection(node, task);
    }

    // ── 汎用フォルダ ──────────────────────────────────
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

    // ── ファイル詳細 ──────────────────────────────────
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
            AddInfoRow("種類",     GetFileType(node.FullPath));
            AddInfoRow("サイズ",   FormatSize(fi.Length));
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

    // ── 子ファイル一覧セクション（共通） ─────────────
    private void AddChildFileSection(FileNode node, TaskItem? ownerTask = null)
    {
        if (node.Children.Count == 0) return;

        // セクションヘッダー行
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

    // ── タスク行（カテゴリー詳細内） ──────────────────
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
        var statusBadge = MakeBadge(task.Status, StatusColor(task.Status));
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

    // ── マッチング ────────────────────────────────────
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

    // ── UI ヘルパー ───────────────────────────────────
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

    private Border MakeBadge(string text, string hexColor, double margin = 0)
    {
        return new Border
        {
            Background = ParseBrush(hexColor, 0.25),
            BorderBrush = ParseBrush(hexColor, 0.7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, margin, 0),
            Child = new TextBlock
            {
                Text = text, FontSize = 11,
                Foreground = ParseBrush(hexColor),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static SolidColorBrush ParseBrush(string hex, double opacity = 1.0)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(Color.FromArgb(
                (byte)(c.A * opacity), c.R, c.G, c.B));
        }
        catch { return new SolidColorBrush(Colors.Gray); }
    }

    private static string StatusColor(string status) => status switch
    {
        "完了"     => "#4CAF50",
        "進行中"   => "#2196F3",
        "未着手"   => "#9E9E9E",
        "保留"     => "#FF9800",
        "レビュー中" => "#9C27B0",
        _          => "#607D8B"
    };

    private static string PriorityColor(string priority) => priority switch
    {
        "高"  => "#EF5350",
        "中"  => "#FFA726",
        "低"  => "#78909C",
        _     => "#607D8B"
    };

    // ── 列カスタマイズ: 表示中の列 ─────────────────────
    private List<string> VisibleColumns =>
        _columnOrder.Where(c => !_hiddenColumns.Contains(c)).ToList();

    // ── テーブルヘッダー ─────────────────────────────
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
            Background      = (System.Windows.Media.Brush)FindResource("BgSecondaryBrush"),
            BorderBrush     = (System.Windows.Media.Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(4, 5, 4, 5),
            Child           = g
        };
    }

    // ── 子アイテム1行 ────────────────────────────────
    private Border BuildChildRow(FileNode child, TaskItem? ownerTask = null)
    {
        // ファイルメタデータ
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
            type     = GetFileType(child.FullPath);
            size     = FormatSize(fi.Length);
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
                // 名前列（アイコン + テキスト）
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
            BorderBrush     = (System.Windows.Media.Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(4, 6, 4, 6),
            Cursor          = System.Windows.Input.Cursors.Hand,
            Child           = g
        };

        // 進捗タグ右クリックメニュー（ファイルのみ）
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

        border.MouseEnter      += (_, _) => border.Background = (Brush)FindResource("BgSecondaryBrush");
        border.MouseLeave      += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) =>
        {
            if (child.IsDirectory) Process.Start("explorer.exe", child.FullPath);
            else Process.Start(new ProcessStartInfo(child.FullPath) { UseShellExecute = true });
        };
        return border;
    }

    // ── 進捗タグのトグル ─────────────────────────────
    private void ToggleProgressTag(TaskItem task, string filePath)
    {
        var existing = task.ProgressTagFiles
            .FirstOrDefault(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            task.ProgressTagFiles.Remove(existing);
        else
            task.ProgressTagFiles.Add(filePath);

        _vm.ProjectService.MarkDirtyAndSave();
        // 詳細パネルを再描画して 📌 表示を更新
        if (_selectedNode != null)
            FolderTree_SelectedItemChanged(FolderTree, new RoutedPropertyChangedEventArgs<object>(null!, _selectedNode));
    }

    // ── 進捗反映タグセクション ───────────────────────
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

        var tagged = task.ProgressTagFiles
            .Where(File.Exists)
            .ToList();

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

    // ── 列カスタマイズボタン ─────────────────────────
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

        // 各列の CheckBox + 上下ボタン
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

                // チェックボックス
                var cb = new CheckBox
                {
                    Content   = col,
                    IsChecked = !_hiddenColumns.Contains(col),
                    FontSize  = 13,
                    Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 140,
                    IsEnabled = col != "名前"  // 名前列は常に表示
                };
                cb.Checked   += (_, _) => { _hiddenColumns.Remove(captCol); };
                cb.Unchecked += (_, _) => { _hiddenColumns.Add(captCol); };
                rowSp.Children.Add(cb);

                // 上へ
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

                // 下へ
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

        // OK ボタン
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
        btnOk.Click += (_, _) =>
        {
            win.DialogResult = true;
        };
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

        // 設定を一時コピーして、キャンセル時に戻せるよう
        var backupOrder  = new List<string>(_columnOrder);
        var backupHidden = new HashSet<string>(_hiddenColumns);

        if (win.ShowDialog() == true)
        {
            // 適用: 現在ノードを再表示
            if (_selectedNode != null)
                FolderTree_SelectedItemChanged(FolderTree,
                    new RoutedPropertyChangedEventArgs<object>(null!, _selectedNode));
        }
        else
        {
            // キャンセル: 設定を戻す
            _columnOrder.Clear();
            _columnOrder.AddRange(backupOrder);
            _hiddenColumns.Clear();
            foreach (var h in backupHidden) _hiddenColumns.Add(h);
        }
    }

    // ── Grid生成（可変列）────────────────────────────
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

    // ── セル追加 ─────────────────────────────────────
    private void AddCell(Grid g, string text, int col, bool isHeader,
                         TextAlignment align = TextAlignment.Left)
    {
        var dimBrush  = (System.Windows.Media.Brush)FindResource("TextDimBrush");
        var mainBrush = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");

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

    // ── 情報行（ラベル + 値）────────────────────────
    private void AddInfoRow(string label, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock
        {
            Text       = label,
            FontSize   = 12,
            Foreground = (System.Windows.Media.Brush)FindResource("TextDimBrush")
        };
        var v = new TextBlock
        {
            Text       = value,
            FontSize   = 12,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            TextWrapping = System.Windows.TextWrapping.Wrap
        };
        Grid.SetColumn(l, 0); Grid.SetColumn(v, 1);
        g.Children.Add(l); g.Children.Add(v);
        DetailPanel.Children.Add(g);
    }

    // ── サイズ整形（エクスプローラー風）───────────────
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)          return $"{bytes} バイト";
        if (bytes < 1024 * 1024)   return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    // ── ファイル種類（拡張子→日本語表記）────────────
    private static string GetFileType(string path)
    {
        var ext = Path.GetExtension(path).ToUpperInvariant();
        return ext switch
        {
            ".TXT"            => "テキスト ドキュメント",
            ".PDF"            => "PDF ドキュメント",
            ".XLSX"           => "Microsoft Excel ワークシート",
            ".XLS"            => "Microsoft Excel 97-2003 ワークシート",
            ".DOCX"           => "Microsoft Word ドキュメント",
            ".DOC"            => "Microsoft Word 97-2003 ドキュメント",
            ".PPTX"           => "Microsoft PowerPoint プレゼンテーション",
            ".PPT"            => "Microsoft PowerPoint 97-2003 プレゼンテーション",
            ".PNG"            => "PNG イメージ",
            ".JPG" or ".JPEG" => "JPEG イメージ",
            ".GIF"            => "GIF イメージ",
            ".BMP"            => "ビットマップ イメージ",
            ".ZIP"            => "圧縮 (zip 形式) フォルダー",
            ".RAR"            => "RAR アーカイブ",
            ".7Z"             => "7-Zip アーカイブ",
            ".CS"             => "Visual C# ソース ファイル",
            ".PY"             => "Python スクリプト",
            ".JS"             => "JavaScript ファイル",
            ".TS"             => "TypeScript ファイル",
            ".JSON"           => "JSON ファイル",
            ".XML"            => "XML ドキュメント",
            ".CSV"            => "CSV ファイル",
            ".MP4"            => "MP4 ビデオ ファイル",
            ".MP3"            => "MP3 オーディオ ファイル",
            ".HTML" or ".HTM" => "HTML ドキュメント",
            ".MD"             => "Markdown ファイル",
            ""                => "ファイル",
            _                 => $"{ext.TrimStart('.')} ファイル"
        };
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    // ── コンテキストメニュー表示前フック ─────────────────
    private void TreeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        // 選択中ノードに合わせて項目の有効/無効を調整
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

    // ── ファイル操作：開く ────────────────────────────────
    private void FileOp_Open_Click(object sender, RoutedEventArgs e)
    {
        var node = GetTargetNode(sender);
        if (node == null) return;

        if (node.IsDirectory)
            Process.Start("explorer.exe", node.FullPath);
        else
            Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true });
    }

    // ── ファイル操作：新規フォルダ ────────────────────────
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
            Refresh();
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"フォルダ作成に失敗しました:\n{ex.Message}", "エラー", Window.GetWindow(this));
        }
    }

    // ── ファイル操作：新規ファイル ────────────────────────
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
            Refresh();
            // 作成後すぐ開く
            Process.Start(new ProcessStartInfo(newPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"ファイル作成に失敗しました:\n{ex.Message}", "エラー", Window.GetWindow(this));
        }
    }

    // ── ファイル操作：名前変更 ────────────────────────────
    private void FileOp_Rename_Click(object sender, RoutedEventArgs e)
    {
        var node = GetTargetNode(sender);
        if (node == null) return;
        if (IsProtectedPath(node.FullPath, allowRenameRoot: false))
        { AppDialog.ShowWarning("このファイル/フォルダは変更できません", "保護済み", Window.GetWindow(this)); return; }

        var oldName = node.IsDirectory ? Path.GetFileName(node.FullPath) : Path.GetFileName(node.FullPath);
        var newName = PromptName("新しい名前を入力してください", oldName);
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

        var parent  = Path.GetDirectoryName(node.FullPath)!;
        var newPath = Path.Combine(parent, newName);
        try
        {
            if (node.IsDirectory) Directory.Move(node.FullPath, newPath);
            else                  File.Move(node.FullPath, newPath);

            // データモデル上のパスも更新
            UpdateModelPath(node.FullPath, newPath);
            Refresh();
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"名前変更に失敗しました:\n{ex.Message}", "エラー", Window.GetWindow(this));
        }
    }

    // ── ファイル操作：パスをコピー ────────────────────────
    private void FileOp_CopyPath_Click(object sender, RoutedEventArgs e)
    {
        var node = GetTargetNode(sender);
        if (node == null) return;
        try { Clipboard.SetText(node.FullPath); }
        catch { }
    }

    // ── ファイル操作：削除 ────────────────────────────────
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
                // カテゴリー/タスクの参照を外す
                ClearModelReference(node.FullPath);
            }
            else
            {
                File.Delete(node.FullPath);
            }
            _selectedNode = null;
            UpdateToolbarState();
            Refresh();
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"削除に失敗しました:\n{ex.Message}", "エラー", Window.GetWindow(this));
        }
    }

    // ── 保護パス判定 ─────────────────────────────────────
    /// <summary>変更・削除できないパスかどうか。</summary>
    private bool IsProtectedPath(string fullPath, bool allowRenameRoot)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) return false;

        // プロジェクトルートは削除不可（リネームも不可）
        if (string.Equals(Path.GetFullPath(fullPath),
                          Path.GetFullPath(project.Settings.ProjectPath),
                          StringComparison.OrdinalIgnoreCase)) return !allowRenameRoot;

        // project_data.json / .bak は常に保護
        var name = Path.GetFileName(fullPath);
        if (name.EndsWith("project_data.json", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith(".json.bak",         StringComparison.OrdinalIgnoreCase)) return true;

        // 作業完了フォルダは保護
        if (name == "作業完了") return true;

        return false;
    }

    // ── データモデルのパス更新（リネーム後） ──────────────
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

    // ── データモデルの参照解除（削除後） ──────────────────
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

    // ── ヘルパー ──────────────────────────────────────────
    /// <summary>ツールバーボタン or コンテキストメニューどちらから呼ばれても対象ノードを返す。</summary>
    private FileNode? GetTargetNode(object senderObj)
    {
        // コンテキストメニュー経由の場合は現在選択中のノードを使う
        return _selectedNode;
    }

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

    private void Organize_Click(object sender, RoutedEventArgs e)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) { AppDialog.ShowInfo("プロジェクトを開いてください", "確認", Window.GetWindow(this)); return; }

        var rootPath = project.Settings.ProjectPath;
        if (!Directory.Exists(rootPath)) { AppDialog.ShowInfo("プロジェクトフォルダが見つかりません", "確認", Window.GetWindow(this)); return; }

        var misplaced = new List<(string FilePath, string Reason)>();

        // 紐づき済みカテゴリーフォルダのフルパスセット
        var linkedCatPaths = new HashSet<string>(
            project.Categories
                .Where(c => !string.IsNullOrEmpty(c.FolderPath) && Directory.Exists(c.FolderPath))
                .Select(c => Path.GetFullPath(c.FolderPath)),
            StringComparer.OrdinalIgnoreCase);

        // 紐づき済みタスクフォルダのフルパスセット
        var linkedTaskPaths = new HashSet<string>(
            project.Tasks
                .Where(t => !string.IsNullOrEmpty(t.FolderPath) && Directory.Exists(t.FolderPath))
                .Select(t => Path.GetFullPath(t.FolderPath)),
            StringComparer.OrdinalIgnoreCase);

        // プロジェクトルート直下:
        //   ファイル → JSON / .json.bak 以外は整理対象
        //   フォルダ → カテゴリーに紐づいていない・"作業完了" 以外は整理対象
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
            if (linkedCatPaths.Contains(full)) continue;          // カテゴリーに紐づき済み
            if (Path.GetFileName(d) == "作業完了") continue;       // 完了フォルダ
            misplaced.Add((d, "プロジェクトルート直下（未紐づけフォルダ）"));
        }

        // カテゴリーフォルダ直下:
        //   ファイル → すべて整理対象
        //   フォルダ → タスクに紐づいていないものは整理対象
        foreach (var cat in project.Categories)
        {
            if (!Directory.Exists(cat.FolderPath)) continue;
            foreach (var f in Directory.GetFiles(cat.FolderPath))
                misplaced.Add((f, $"カテゴリー直下ファイル ({cat.Name})"));
            foreach (var d in Directory.GetDirectories(cat.FolderPath))
            {
                var full = Path.GetFullPath(d);
                if (linkedTaskPaths.Contains(full)) continue;     // タスクに紐づき済み
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

    private void ShowOrganizeDialog(List<(string FilePath, string Reason)> files,
                                    TKer.Models.ProjectData project, string rootPath)
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

        var fg      = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(207, 207, 207));
        var bg      = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32,  32,  32));
        var cardBg  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(47,  47,  47));
        var dim     = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 119, 116));
        var cbBg    = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(55,  55,  55));

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
        var destMap = new Dictionary<string, System.Windows.Controls.ComboBox>();

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
                Text = System.IO.Path.GetFileName(fp), FontSize = 13, FontWeight = FontWeights.Bold, Foreground = fg
            });
            leftSp.Children.Add(new TextBlock { Text = reason, FontSize = 11, Foreground = dim });

            var cb = new System.Windows.Controls.ComboBox
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
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 131, 226)),
            Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold,
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
                var destDir = sel?.Path as string ?? rootPath;
                var srcName = System.IO.Path.GetFileName(src);
                var destPath = System.IO.Path.Combine(destDir, srcName);
                try
                {
                    if (destPath == src) continue;
                    // 名前衝突時は _moved を付与
                    if (System.IO.File.Exists(destPath) || System.IO.Directory.Exists(destPath))
                    {
                        var nameOnly = System.IO.Path.GetFileNameWithoutExtension(src);
                        var ext      = System.IO.Path.GetExtension(src);
                        destPath = System.IO.Path.Combine(destDir, $"{nameOnly}_moved{ext}");
                    }
                    bool isDir = System.IO.Directory.Exists(src);
                    if (isDir)
                        System.IO.Directory.Move(src, destPath);
                    else
                        System.IO.File.Move(src, destPath);
                    moved++;
                }
                catch { failed++; }
            }
            win.Close();
            if (failed > 0)
                AppDialog.ShowWarning($"移動完了: {moved} 件\n失敗: {failed} 件", "フォルダ整理", Window.GetWindow(this));
            else
                AppDialog.ShowInfo($"移動完了: {moved} 件", "フォルダ整理", Window.GetWindow(this));
            Refresh();
        };

        btnPanel.Children.Add(cancelBtn); btnPanel.Children.Add(moveBtn);
        outer.Children.Add(btnPanel);
        win.Content = new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        win.ShowDialog();
    }

    private void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm.ProjectService.CurrentProject?.Settings.ProjectPath;
        if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
            Process.Start("explorer.exe", path);
    }
}

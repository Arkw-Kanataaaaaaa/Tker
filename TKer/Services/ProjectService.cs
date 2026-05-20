using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using TKer.Helpers;
using TKer.Models;

namespace TKer.Services;

/// <summary>プロジェクトデータの読み書き・カテゴリー/タスク管理を担当するサービス。</summary>
public class ProjectService
{
    private const string DATA_FILE_NAME       = "project_data.json";
    private const string BACKUP_SUFFIX        = ".bak";
    private const string COMPLETED_FOLDER_NAME = "作業完了";

    public ProjectData? CurrentProject  { get; private set; }
    public string?      ProjectFilePath { get; private set; }
    public bool         IsLoaded        => CurrentProject != null;

    // ⑦ 自動保存タイマー
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _hasUnsavedChanges = false;

    /// <summary>フォルダ操作用非同期キュー</summary>
    public readonly FolderOperationQueue FolderQueue = new();

    public event EventHandler? ProjectChanged;

    /// <summary>自動保存タイマーを初期化してサービスを生成する。</summary>
    public ProjectService()
    {
        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _autoSaveTimer.Tick += (_, _) =>
        {
            if (_hasUnsavedChanges) SaveProject();
        };
    }

    // =====================================================
    // プロジェクト作成
    // =====================================================
    /// <summary>指定パスに新規プロジェクトを作成してデータを保存する。</summary>
    public void CreateProject(string basePath, string projectName, string description = "")
    {
        var projectPath = Path.Combine(basePath, projectName);
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(projectPath, COMPLETED_FOLDER_NAME));

        CurrentProject = new ProjectData
        {
            Settings = new ProjectSettings
            {
                ProjectName = projectName,
                ProjectPath = projectPath,
                Description = description,
                CreatedAt   = DateTime.Now
            }
        };

        ProjectFilePath = Path.Combine(projectPath, DATA_FILE_NAME);
        SaveProject();
        _autoSaveTimer.Start();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    // =====================================================
    // プロジェクト読み込み
    // =====================================================
    /// <summary>指定ファイルパスからプロジェクトデータを読み込む。</summary>
    public bool LoadProject(string filePath)
    {
        try
        {
            // ⑥ 競合チェック：読み込み前に最終更新日を記録
            var json = File.ReadAllText(filePath);
            var loaded = JsonConvert.DeserializeObject<ProjectData>(json);
            if (loaded == null) return false;
            CurrentProject  = loaded;
            ProjectFilePath = filePath;
            _hasUnsavedChanges = false;

            // 既存プロジェクトの Order 移行：全て 0 ならリスト順で採番
            if (CurrentProject.Categories.Count > 0 &&
                CurrentProject.Categories.All(c => c.Order == 0))
            {
                for (int i = 0; i < CurrentProject.Categories.Count; i++)
                    CurrentProject.Categories[i].Order = i + 1;
                _hasUnsavedChanges = true;
            }

            _autoSaveTimer.Start();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch { return false; }
    }

    // =====================================================
    // プロジェクト保存（バックアップ付き） ⑦
    // =====================================================
    /// <summary>バックアップを作成してからプロジェクトデータをJSONで保存する。</summary>
    public void SaveProject()
    {
        if (CurrentProject == null || ProjectFilePath == null) return;
        CurrentProject.LastSaved = DateTime.Now;

        // バックアップ作成
        if (File.Exists(ProjectFilePath))
        {
            var backup = ProjectFilePath + BACKUP_SUFFIX;
            File.Copy(ProjectFilePath, backup, overwrite: true);
        }

        var json = JsonConvert.SerializeObject(CurrentProject, Formatting.Indented);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        // 一時ファイルに書き出してから置換し、書き込み途中のクラッシュでも本体が壊れないようにする
        var tmp = ProjectFilePath + ".tmp";
        File.WriteAllText(tmp, json, encoding);
        if (File.Exists(ProjectFilePath))
            File.Replace(tmp, ProjectFilePath, null);
        else
            File.Move(tmp, ProjectFilePath);
        _hasUnsavedChanges = false;
    }

    // =====================================================
    // ⑥ プロジェクト移動後のパス再マッピング
    // =====================================================
    /// <summary>旧ベースパスを新ベースパスに一括置換してプロジェクトを保存する。</summary>
    public void RemapPaths(string oldBase, string newBase)
    {
        if (CurrentProject == null) return;
        CurrentProject.Settings.ProjectPath = newBase;
        foreach (var cat in CurrentProject.Categories)
            cat.FolderPath = RebasePath(cat.FolderPath, oldBase, newBase);
        foreach (var task in CurrentProject.Tasks)
            task.FolderPath = RebasePath(task.FolderPath, oldBase, newBase);
        SaveAndNotifyProject();
    }

    /// <summary>パスの先頭が oldBase の場合のみ newBase に置き換える（部分文字列誤置換を防ぐ）</summary>
    private static string RebasePath(string path, string oldBase, string newBase)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(oldBase, StringComparison.OrdinalIgnoreCase))
            return newBase + path[oldBase.Length..];
        return path;
    }

    /// <summary>プロジェクトフォルダが移動されていれば自動修正して読み込む</summary>
    public bool LoadProjectWithAutoRemap(string filePath)
    {
        if (!LoadProject(filePath)) return false;
        if (CurrentProject == null) return false;

        var newBase = Path.GetDirectoryName(filePath)!;
        var oldBase = CurrentProject.Settings.ProjectPath;
        if (!string.IsNullOrEmpty(oldBase) &&
            !oldBase.Equals(newBase, StringComparison.OrdinalIgnoreCase))
        {
            RemapPaths(oldBase, newBase);
        }
        return true;
    }

    // =====================================================
    // ⑥ 競合チェック付きリロード
    // =====================================================
    /// <summary>ディスク上のファイルが最終保存より新しい場合にリロードする。</summary>
    public bool TryReloadIfNewer()
    {
        if (ProjectFilePath == null || !File.Exists(ProjectFilePath)) return false;
        var fileTime = File.GetLastWriteTime(ProjectFilePath);
        if (CurrentProject != null && fileTime <= CurrentProject.LastSaved.AddSeconds(2)) return false;

        var json = File.ReadAllText(ProjectFilePath);
        var reloaded = JsonConvert.DeserializeObject<ProjectData>(json);
        if (reloaded == null) return false;
        CurrentProject = reloaded;
        _hasUnsavedChanges = false;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // =====================================================
    // ⑲ プロジェクト設定更新
    // =====================================================
    /// <summary>プロジェクト名・説明・バージョン・担当者を更新して保存する。</summary>
    public void UpdateProjectSettings(string name, string description, string version, string manager)
    {
        if (CurrentProject == null) return;
        CurrentProject.Settings.ProjectName = name;
        CurrentProject.Settings.Description = description;
        CurrentProject.ProjectVersion        = version;
        CurrentProject.Manager               = manager;
        SaveAndNotifyProject();
    }

    // =====================================================
    // カテゴリー追加
    // =====================================================
    /// <summary>新規カテゴリーを追加してフォルダを作成し、プロジェクトを保存する。</summary>
    public Category AddCategory(string name, string description = "", string color = "#3D7EFF")
    {
        if (CurrentProject == null) throw new InvalidOperationException("プロジェクト未ロード");

        var id    = GenerateCategoryId();
        var order = CurrentProject.Categories.Count + 1; // 新しいカテゴリーは末尾の順番
        var category = new Category
        {
            Id          = id,
            Name        = name,
            Description = description,
            Color       = color,
            Order       = order,
            CreatedAt   = DateTime.Now
        };

        // 優先度番号をプレフィックスとしたフォルダ名
        var folderName = $"{order:D3}_{StringHelper.SanitizeFileName(name)}";
        var folderPath = Path.Combine(CurrentProject.Settings.ProjectPath, folderName);
        folderPath = EnsureUniqueFolder(folderPath);
        Directory.CreateDirectory(folderPath);
        category.FolderPath    = folderPath;
        category.FolderCreated = true;

        CurrentProject.Categories.Add(category);
        SaveAndNotifyProject();
        return category;
    }

    // =====================================================
    // カテゴリー順序変更（優先度リネーム）
    // =====================================================
    /// <summary>
    /// カテゴリーを指定順に並べ替え、フォルダを非同期でリネームする。
    /// orderedIds: 新しい順番のカテゴリーIDリスト（先頭が優先度1）
    /// </summary>
    public void ReorderCategories(List<string> orderedIds)
    {
        if (CurrentProject == null) return;

        // 1. Order 値を更新
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var cat = CurrentProject.Categories.FirstOrDefault(c => c.Id == orderedIds[i]);
            if (cat != null) cat.Order = i + 1;
        }

        // 2. リスト自体を並べ替え
        CurrentProject.Categories.Sort((a, b) => a.Order.CompareTo(b.Order));

        // 3. フォルダリネームが必要なカテゴリーを検出
        var renameTargets = new List<(Category cat, string oldPath, string finalName)>();
        foreach (var cat in CurrentProject.Categories)
        {
            if (!cat.FolderCreated || !Directory.Exists(cat.FolderPath)) continue;
            var parentDir  = Path.GetDirectoryName(cat.FolderPath)!;
            var expectName = $"{cat.Order:D3}_{StringHelper.SanitizeFileName(cat.Name)}";
            var expectPath = Path.Combine(parentDir, expectName);
            if (!string.Equals(cat.FolderPath, expectPath, StringComparison.OrdinalIgnoreCase))
                renameTargets.Add((cat, cat.FolderPath, expectName));
        }

        if (renameTargets.Count > 0)
        {
            // STEP A: 全対象を一時名にリネーム（競合回避）— 同期実行
            var temps = new List<(Category cat, string tempPath, string finalPath, string oldCatPath)>();
            foreach (var (cat, oldPath, finalName) in renameTargets)
            {
                try
                {
                    if (!Directory.Exists(oldPath)) continue;
                    var parentDir = Path.GetDirectoryName(oldPath)!;
                    var tempPath  = Path.Combine(parentDir, $"PFTMP_{Guid.NewGuid():N}");
                    Directory.Move(oldPath, tempPath);
                    var finalPath = EnsureUniqueFolder(Path.Combine(parentDir, finalName));
                    temps.Add((cat, tempPath, finalPath, oldPath));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ReorderCategories] STEP A 失敗: {ex.Message}");
                }
            }

            // STEP B: 一時名 → 最終名、モデル更新 — 同期実行
            foreach (var (cat, tempPath, finalPath, oldCatPath) in temps)
            {
                try
                {
                    if (!Directory.Exists(tempPath)) continue;
                    Directory.Move(tempPath, finalPath);
                    cat.FolderPath = finalPath;

                    // 配下タスクのパス更新
                    foreach (var t in CurrentProject.Tasks.Where(t => t.CategoryId == cat.Id))
                        if (!string.IsNullOrEmpty(t.FolderPath) &&
                            t.FolderPath.StartsWith(oldCatPath, StringComparison.OrdinalIgnoreCase))
                            t.FolderPath = finalPath + t.FolderPath[oldCatPath.Length..];
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ReorderCategories] STEP B 失敗: {ex.Message}");
                }
            }
        }

        SaveAndNotifyProject();
    }

    /// <summary>カテゴリーの内容を更新し、必要に応じてフォルダをリネームする。</summary>
    public void UpdateCategory(Category category, bool renameFolder = false)
    {
        if (CurrentProject == null) return;
        var existing = CurrentProject.Categories.FirstOrDefault(c => c.Id == category.Id);
        if (existing == null) return;

        // ⑨⑩ カテゴリー名変更時のフォルダリネーム
        if (renameFolder && existing.FolderCreated && Directory.Exists(existing.FolderPath))
        {
            var parentDir  = Path.GetDirectoryName(existing.FolderPath)!;
            var oldPath    = existing.FolderPath;
            var newFolderName = $"{existing.Order:D3}_{StringHelper.SanitizeFileName(category.Name)}";
            var newFolderPath = EnsureUniqueFolder(Path.Combine(parentDir, newFolderName));
            if (!string.Equals(newFolderPath, oldPath, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(oldPath, newFolderPath);
                existing.FolderPath = newFolderPath;
                // 配下タスクのパス更新
                foreach (var t in CurrentProject.Tasks.Where(t => t.CategoryId == existing.Id))
                {
                    if (!string.IsNullOrEmpty(t.FolderPath) &&
                        t.FolderPath.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase))
                        t.FolderPath = newFolderPath + t.FolderPath[oldPath.Length..];
                }
            }
        }

        existing.Name        = category.Name;
        existing.Description = category.Description;
        existing.Color       = category.Color;
        SaveAndNotifyProject();
    }

    /// <summary>カテゴリーと配下のタスクを削除し、オプションでフォルダも削除する。</summary>
    public void DeleteCategory(string categoryId, bool deleteFolder = false)
    {
        if (CurrentProject == null) return;
        var category = CurrentProject.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category == null) return;

        // ⑧ フォルダ削除オプション
        if (deleteFolder && category.FolderCreated && Directory.Exists(category.FolderPath))
        {
            try { Directory.Delete(category.FolderPath, recursive: true); }
            catch { /* 削除失敗は無視 */ }
        }

        var tasks = CurrentProject.Tasks.Where(t => t.CategoryId == categoryId).ToList();
        foreach (var task in tasks) CurrentProject.Tasks.Remove(task);
        CurrentProject.Categories.Remove(category);
        SaveAndNotifyProject();
    }

    // =====================================================
    // タスク追加
    // =====================================================
    /// <summary>新規タスクを追加してフォルダを作成し、プロジェクトを保存する。</summary>
    public TaskItem AddTask(string categoryId, string name, string nameShort,
        string subCategory = "", string environment = "", string assignee = "",
        string priority = "中", string status = "未着手",
        DateTime? plannedStart = null, DateTime? plannedEnd = null,
        string description = "", string notes = "", string tags = "")
    {
        if (CurrentProject == null) throw new InvalidOperationException("プロジェクト未ロード");

        var id        = GenerateTaskId();
        var shortName = string.IsNullOrWhiteSpace(nameShort) ? StringHelper.Truncate(name, 20) : nameShort;

        var task = new TaskItem
        {
            Id               = id,
            CategoryId       = categoryId,
            Name             = name,
            NameShort        = shortName,
            SubCategory      = subCategory,
            Environment      = environment,
            Assignee         = assignee,
            Priority         = priority,
            Status           = status,
            PlannedStartDate = plannedStart,
            PlannedEndDate   = plannedEnd,
            Description      = description,
            Notes            = notes,
            Tags             = tags,
            CreatedAt        = DateTime.Now,
            UpdatedAt        = DateTime.Now
        };

        var category   = CurrentProject.Categories.FirstOrDefault(c => c.Id == categoryId);
        var basePath   = category?.FolderPath ?? CurrentProject.Settings.ProjectPath;
        var folderName = $"{id}_{StringHelper.SanitizeFileName(shortName)}";
        var folderPath = EnsureUniqueFolder(Path.Combine(basePath, folderName));
        Directory.CreateDirectory(folderPath);
        task.FolderPath    = folderPath;
        task.FolderCreated = true;

        CurrentProject.Tasks.Add(task);
        try
        {
            MarkDirtyAndSave();
        }
        catch
        {
            CurrentProject.Tasks.Remove(task);
            try { if (Directory.Exists(folderPath)) Directory.Delete(folderPath, true); } catch { }
            throw;
        }
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return task;
    }

    /// <summary>タスクの内容を更新し、省略名変更時はフォルダもリネームする。</summary>
    public void UpdateTask(TaskItem task, bool renameFolderOnShortNameChange = true)
    {
        if (CurrentProject == null) return;
        var existing = CurrentProject.Tasks.FirstOrDefault(t => t.Id == task.Id);
        if (existing == null) return;

        // ⑨ 省略名変更時のフォルダリネーム
        if (renameFolderOnShortNameChange &&
            existing.NameShort != task.NameShort &&
            existing.FolderCreated &&
            Directory.Exists(existing.FolderPath))
        {
            var parentDir     = Path.GetDirectoryName(existing.FolderPath)!;
            var newFolderName  = $"{existing.Id}_{StringHelper.SanitizeFileName(task.NameShort)}";
            var newFolderPath  = EnsureUniqueFolder(Path.Combine(parentDir, newFolderName));
            Directory.Move(existing.FolderPath, newFolderPath);
            existing.FolderPath = newFolderPath;
        }

        existing.Name             = task.Name;
        existing.NameShort        = task.NameShort;
        existing.CategoryId       = task.CategoryId;
        existing.SubCategory      = task.SubCategory;
        existing.Environment      = task.Environment;
        existing.Assignee         = task.Assignee;
        existing.Priority         = task.Priority;
        existing.Status           = task.Status;
        existing.PlannedStartDate = task.PlannedStartDate;
        existing.PlannedEndDate   = task.PlannedEndDate;
        existing.ActualStartDate  = task.ActualStartDate;
        existing.ActualEndDate    = task.ActualEndDate;
        existing.Description      = task.Description;
        existing.Notes            = task.Notes;
        existing.Tags             = task.Tags;
        existing.DelayApproved    = task.DelayApproved;
        existing.UpdatedAt        = DateTime.Now;

        if (existing.Status == "完了" && existing.FolderCreated && !existing.MovedToComplete)
            MoveTaskFolderToComplete(existing);

        SaveAndNotifyProject();
    }

    /// <summary>タスクを削除し、オプションでフォルダも削除する。</summary>
    public void DeleteTask(string taskId, bool deleteFolder = false)
    {
        if (CurrentProject == null) return;
        var task = CurrentProject.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null) return;

        if (deleteFolder && task.FolderCreated && Directory.Exists(task.FolderPath))
            Directory.Delete(task.FolderPath, recursive: true);

        CurrentProject.Tasks.Remove(task);
        try
        {
            MarkDirtyAndSave();
        }
        catch
        {
            CurrentProject.Tasks.Add(task);
            throw;
        }
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>タスクフォルダを新カテゴリーのフォルダ配下に移動する。</summary>
    public (bool Success, string? Error) TryMoveTaskFolderToCategory(TaskItem task, string newCategoryId)
    {
        if (CurrentProject == null) return (false, "プロジェクト未ロード");
        if (!task.FolderCreated || !Directory.Exists(task.FolderPath)) return (true, null);

        var category = CurrentProject.Categories.FirstOrDefault(c => c.Id == newCategoryId);
        var destBase = category?.FolderPath ?? CurrentProject.Settings.ProjectPath;
        var destPath = EnsureUniqueFolder(Path.Combine(destBase, Path.GetFileName(task.FolderPath)));

        try
        {
            Directory.Move(task.FolderPath, destPath);
            task.FolderPath = destPath;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // =====================================================
    // ⑰ コメント追加
    // =====================================================
    /// <summary>指定タスクにコメントを追加して保存する。</summary>
    public void AddComment(string taskId, string author, string text)
    {
        if (CurrentProject == null) return;
        var task = CurrentProject.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null) return;
        task.Comments.Add(new TaskComment { Author = author, Text = text });
        task.UpdatedAt = DateTime.Now;
        MarkDirtyAndSave();
    }

    // =====================================================
    // 完了フォルダへ移動
    // =====================================================
    /// <summary>タスクフォルダを作業完了フォルダへ移動してフラグを立てる。</summary>
    private void MoveTaskFolderToComplete(TaskItem task)
    {
        if (!Directory.Exists(task.FolderPath)) return;
        var completedPath = Path.Combine(CurrentProject!.Settings.ProjectPath, COMPLETED_FOLDER_NAME);
        Directory.CreateDirectory(completedPath);
        var destPath = Path.Combine(completedPath, Path.GetFileName(task.FolderPath));
        destPath = EnsureUniqueFolder(destPath);
        Directory.Move(task.FolderPath, destPath);
        task.FolderPath       = destPath;
        task.MovedToComplete  = true;
    }

    // =====================================================
    // ⑯ CSV エクスポート
    // =====================================================
    /// <summary>タスク一覧をCSVファイルに書き出す。</summary>
    public void ExportToCsv(string filePath, IEnumerable<TaskItem>? tasks = null)
    {
        if (CurrentProject == null) return;
        var targetTasks = tasks ?? CurrentProject.Tasks;
        var catMap = CurrentProject.Categories.ToDictionary(c => c.Id, c => c.Name);

        var lines = new List<string>
        {
            "ID,カテゴリー,タスク名,中分類,環境,担当者,優先度,ステータス,予定開始,予定終了,実績開始,実績終了,遅延日数,タグ,備考"
        };
        foreach (var t in targetTasks)
        {
            lines.Add(string.Join(",",
                CsvEscape(t.Id),
                CsvEscape(catMap.GetValueOrDefault(t.CategoryId, "")),
                CsvEscape(t.Name),
                CsvEscape(t.SubCategory),
                CsvEscape(t.Environment),
                CsvEscape(t.Assignee),
                CsvEscape(t.Priority),
                CsvEscape(t.Status),
                t.PlannedStartDate?.ToString("yyyy/MM/dd") ?? "",
                t.PlannedEndDate?.ToString("yyyy/MM/dd") ?? "",
                t.ActualStartDate?.ToString("yyyy/MM/dd") ?? "",
                t.ActualEndDate?.ToString("yyyy/MM/dd") ?? "",
                t.DelayDays?.ToString() ?? "",
                CsvEscape(t.Tags),
                CsvEscape(t.Notes)));
        }
        File.WriteAllLines(filePath, lines, System.Text.Encoding.UTF8);
    }

    /// <summary>CSV出力用に文字列をエスケープする。</summary>
    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    // =====================================================
    // フォルダツリー取得
    // =====================================================
    /// <summary>プロジェクトフォルダのファイルノードツリーを返す。</summary>
    public FileNode? GetProjectFolderTree()
    {
        if (CurrentProject == null) return null;
        return BuildFileNode(CurrentProject.Settings.ProjectPath, 0, 3);
    }

    /// <summary>タスクフォルダのファイルノードツリーを返す。</summary>
    public FileNode? GetTaskFolderTree(TaskItem task)
    {
        if (!task.FolderCreated || !Directory.Exists(task.FolderPath)) return null;
        return BuildFileNode(task.FolderPath, 0, 2);
    }

    /// <summary>指定パスを起点に再帰的なFileNodeを構築する。</summary>
    private static FileNode BuildFileNode(string path, int depth, int maxDepth)
    {
        var node = new FileNode
        {
            Name        = Path.GetFileName(path),
            FullPath    = path,
            IsDirectory = true,
            IsExpanded  = depth == 0
        };
        if (depth >= maxDepth) return node;
        try
        {
            foreach (var dir  in Directory.GetDirectories(path).OrderBy(d => d))
                node.Children.Add(BuildFileNode(dir, depth + 1, maxDepth));
            foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
                node.Children.Add(new FileNode
                    { Name = Path.GetFileName(file), FullPath = file, IsDirectory = false });
        }
        catch { }
        return node;
    }

    // =====================================================
    // ガントデータ生成  ⑭
    // =====================================================
    /// <summary>ガントチャート用の行データを生成して返す。</summary>
    public List<GanttRow> GetGanttRows(string? filterCategoryId = null,
                                        string? filterStatus     = null,
                                        bool    hideCompleted    = false)
    {
        if (CurrentProject == null) return new();

        var rows = new List<GanttRow>();
        foreach (var cat in CurrentProject.Categories)
        {
            if (filterCategoryId != null && cat.Id != filterCategoryId) continue;

            var tasks = CurrentProject.Tasks
                .Where(t => t.CategoryId == cat.Id)
                .Where(t => filterStatus == null || t.Status == filterStatus)
                .Where(t => !hideCompleted || t.Status != "完了")
                .ToList();

            rows.Add(new GanttRow { Id = cat.Id, Label = cat.Name, IndentLevel = 0, IsCategory = true });

            foreach (var group in tasks.GroupBy(t => t.SubCategory).OrderBy(g => g.Key))
            {
                if (!string.IsNullOrEmpty(group.Key))
                    rows.Add(new GanttRow
                    {
                        Id          = $"sub_{cat.Id}_{group.Key}",
                        Label       = group.Key,
                        IndentLevel = 1,
                        IsCategory  = true
                    });

                foreach (var task in group.OrderBy(t => t.PlannedStartDate))
                    rows.Add(new GanttRow
                    {
                        Id           = task.Id,
                        Label        = task.Name,
                        IndentLevel  = string.IsNullOrEmpty(group.Key) ? 1 : 2,
                        IsCategory   = false,
                        PlannedStart = task.PlannedStartDate,
                        PlannedEnd   = task.PlannedEndDate,
                        ActualStart  = task.ActualStartDate,
                        ActualEnd    = task.ActualEndDate,
                        Status       = task.Status,
                        Priority     = task.Priority,
                        Assignee     = task.Assignee,
                        Task         = task
                    });
            }
        }
        return rows;
    }

    /// <summary>全タスクの日付から最小・最大日を取得してガント表示範囲を決定する。</summary>
    public (DateTime min, DateTime max) GetTaskDateRange()
    {
        if (CurrentProject == null || !CurrentProject.Tasks.Any())
            return (DateTime.Today.AddDays(-7), DateTime.Today.AddDays(60));

        var allDates = CurrentProject.Tasks
            .SelectMany(t => new[]
            {
                t.PlannedStartDate, t.PlannedEndDate,
                t.ActualStartDate,  t.ActualEndDate
            })
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        return allDates.Any()
            ? (allDates.Min().AddDays(-3), allDates.Max().AddDays(3))
            : (DateTime.Today.AddDays(-7), DateTime.Today.AddDays(60));
    }

    // =====================================================
    // カレンダーデータ生成  ⑭
    // =====================================================
    /// <summary>指定年月のカレンダーセル（42日分）を生成してタスクを割り当てる。</summary>
    public List<CalendarCell> GetCalendarCells(int year, int month, bool hideCompleted = false)
    {
        if (CurrentProject == null) return new();

        var cells     = new List<CalendarCell>();
        var firstDay  = new DateTime(year, month, 1);
        var startDate = firstDay.AddDays(-(int)firstDay.DayOfWeek);

        var tasks = CurrentProject.Tasks
            .Where(t => !hideCompleted || t.Status != "完了")
            .ToList();

        for (int i = 0; i < 42; i++)
        {
            var date = startDate.AddDays(i);
            var cell = new CalendarCell { Date = date, IsCurrentMonth = date.Month == month };

            foreach (var t in tasks)
            {
                if (t.PlannedStartDate.HasValue && t.PlannedEndDate.HasValue &&
                    date >= t.PlannedStartDate.Value.Date && date <= t.PlannedEndDate.Value.Date)
                    cell.PlannedTasks.Add(t);

                if (t.ActualStartDate.HasValue && t.ActualEndDate.HasValue &&
                    date >= t.ActualStartDate.Value.Date && date <= t.ActualEndDate.Value.Date)
                    cell.ActualTasks.Add(t);
            }
            cells.Add(cell);
        }
        return cells;
    }

    // =====================================================
    // Excel インポート
    // =====================================================
    /// <summary>Excelファイルからカテゴリーとタスクを一括インポートして保存する。</summary>
    public void ImportFromExcel(string excelPath)
    {
        if (CurrentProject == null) return;
        using var workbook = new ClosedXML.Excel.XLWorkbook(excelPath);
        var ws = workbook.Worksheets.First();

        string? currentCategoryId = null;
        string? currentSubCategory = null;

        for (int row = 7; row <= (ws.LastRowUsed()?.RowNumber() ?? 100); row++)
        {
            var numCell = ws.Cell(row, 2).GetString();
            if (string.IsNullOrWhiteSpace(numCell)) continue;

            var bigCat   = ws.Cell(row, 3).GetString();
            var midCat   = ws.Cell(row, 4).GetString();
            var env      = ws.Cell(row, 5).GetString();
            var taskName = ws.Cell(row, 6).GetString();
            var assignee = ws.Cell(row, 7).GetString();
            var priority = ws.Cell(row, 12).GetString();
            var status   = ws.Cell(row, 13).GetString();
            var notes    = ws.Cell(row, 14).GetString();

            DateTime? planStart = ws.Cell(row, 8).TryGetValue<DateTime>(out var ps) ? ps : null;
            DateTime? planEnd   = ws.Cell(row, 9).TryGetValue<DateTime>(out var pe) ? pe : null;
            DateTime? actStart  = ws.Cell(row, 10).TryGetValue<DateTime>(out var als) ? als : null;
            DateTime? actEnd    = ws.Cell(row, 11).TryGetValue<DateTime>(out var ale) ? ale : null;

            if (!string.IsNullOrWhiteSpace(bigCat) && bigCat != "*****")
            {
                var cat = AddCategory(bigCat);
                currentCategoryId  = cat.Id;
                currentSubCategory = null;
                continue;
            }
            if (!string.IsNullOrWhiteSpace(midCat) && midCat != "*****" && string.IsNullOrWhiteSpace(taskName))
            {
                currentSubCategory = midCat;
                continue;
            }
            if (!string.IsNullOrWhiteSpace(taskName) && taskName != "*****" && currentCategoryId != null)
            {
                var mappedStatus   = status   is "対応中" or "完了" or "レビュー中" ? status : "未着手";
                var mappedPriority = priority is "高" or "低" ? priority : "中";

                var task = AddTask(currentCategoryId, taskName, StringHelper.Truncate(taskName, 20),
                    currentSubCategory ?? "", env, assignee,
                    mappedPriority, mappedStatus, planStart, planEnd, notes: notes ?? "");

                if (actStart.HasValue) task.ActualStartDate = actStart;
                if (actEnd.HasValue)   task.ActualEndDate   = actEnd;
            }
        }

        SaveAndNotifyProject();
    }

    // =====================================================
    // 統計
    // =====================================================
    /// <summary>全タスクの合計・完了・対応中・未着手の件数を返す。</summary>
    public (int total, int done, int wip, int todo) GetTaskStats()
    {
        if (CurrentProject == null) return (0, 0, 0, 0);
        var t = CurrentProject.Tasks;
        return (t.Count, t.Count(x => x.Status == "完了"),
                t.Count(x => x.Status == "対応中"), t.Count(x => x.Status == "未着手"));
    }

    // =====================================================
    // ヘルパー
    // =====================================================

    // ② MAX ID+1 方式で衝突なし
    /// <summary>既存カテゴリーIDの最大値+1から新規カテゴリーIDを生成する。</summary>
    private string GenerateCategoryId()
    {
        var max = CurrentProject!.Categories
            .Select(c => int.TryParse(c.Id.Length > 3 ? c.Id[3..] : "0", out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        return $"CAT{max + 1:D3}";
    }

    /// <summary>既存タスクIDの最大値+1から新規タスクIDを生成する。</summary>
    private string GenerateTaskId()
    {
        var max = CurrentProject!.Tasks
            .Select(t => int.TryParse(t.Id.Length > 3 ? t.Id[3..] : "0", out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        return $"TSK{max + 1:D4}";
    }

    // ⑤ フォルダ衝突回避
    /// <summary>同名フォルダが存在する場合は末尾に連番を付けて重複しないパスを返す。</summary>
    private static string EnsureUniqueFolder(string path)
    {
        if (!Directory.Exists(path)) return path;
        int i = 1;
        while (Directory.Exists($"{path}_{i}")) i++;
        return $"{path}_{i}";
    }

    /// <summary>未保存フラグを立ててプロジェクトを即時保存する。</summary>
    public void MarkDirtyAndSave()
    {
        _hasUnsavedChanges = true;
        SaveProject(); // 即時保存（自動保存は補完用）
    }

    /// <summary>プロジェクトを保存してProjectChangedイベントを発火する。</summary>
    private void SaveAndNotifyProject()
    {
        MarkDirtyAndSave();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    // =====================================================
    // 表作成ツール CRUD
    // =====================================================
    /// <summary>新規カスタムテーブルを作成してプロジェクトに追加する。</summary>
    public CustomTable CreateTable(string name)
    {
        var table = new CustomTable { Name = name };
        CurrentProject!.CustomTables.Add(table);
        MarkDirtyAndSave();
        return table;
    }

    /// <summary>指定IDのカスタムテーブルを削除して保存する。</summary>
    public void DeleteTable(string tableId)
    {
        CurrentProject!.CustomTables.RemoveAll(t => t.Id == tableId);
        MarkDirtyAndSave();
    }

    /// <summary>テーブル定義（カラム構成など）の変更をプロジェクトに保存する。</summary>
    public void SaveTableDefinition(CustomTable table)
    {
        MarkDirtyAndSave();
    }

    /// <summary>テーブルに新規行を追加し、自動連番カラムを採番して保存する。</summary>
    public TableRow AddRow(CustomTable table)
    {
        int nextNum = table.Rows.Count + 1;
        var row = new TableRow();
        foreach (var col in table.Columns.Where(c => c.Type == "autonumber"))
            row.Cells[col.Id] = nextNum.ToString();
        table.Rows.Add(row);
        MarkDirtyAndSave();
        return row;
    }

    /// <summary>指定行IDをテーブルから削除し、自動連番を振り直して保存する。</summary>
    public void DeleteRows(CustomTable table, IEnumerable<string> rowIds)
    {
        var ids = new HashSet<string>(rowIds);
        table.Rows.RemoveAll(r => ids.Contains(r.Id));
        // recompute autonumber columns
        int num = 1;
        foreach (var row in table.Rows)
            foreach (var col in table.Columns.Where(c => c.Type == "autonumber"))
                row.Cells[col.Id] = (num++).ToString();
        MarkDirtyAndSave();
    }

    /// <summary>指定行・カラムのセル値を更新して保存する。</summary>
    public void UpdateCell(CustomTable table, string rowId, string columnId, string value)
    {
        var row = table.Rows.FirstOrDefault(r => r.Id == rowId);
        if (row == null) return;
        row.Cells[columnId] = value;
        MarkDirtyAndSave();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// 成果物まとめ機能
/// 全タスク（または指定カテゴリー）の完了フォルダ内ファイルを
/// 指定先に集約・ZIP化・インデックス生成する。
/// </summary>
public class DeliverableService
{
    private readonly ProjectService _projectService;

    public DeliverableService(ProjectService projectService)
    {
        _projectService = projectService;
    }

    // =====================================================
    // 成果物まとめ設定
    // =====================================================
    public class CollectOptions
    {
        /// <summary>集約先ベースパス（省略時はプロジェクトフォルダ内に生成）</summary>
        public string? OutputBasePath    { get; set; }
        /// <summary>出力フォルダ名（日時自動付与）</summary>
        public string  OutputFolderName  { get; set; } = "成果物まとめ";
        /// <summary>カテゴリーID でフィルター（null=全カテゴリー）</summary>
        public string? FilterCategoryId  { get; set; }
        /// <summary>完了タスクのみ対象にするか（false なら全タスク）</summary>
        public bool    CompletedOnly     { get; set; } = true;
        /// <summary>カテゴリーごとにサブフォルダを作るか</summary>
        public bool    GroupByCategory   { get; set; } = true;
        /// <summary>ZIP ファイルも生成するか</summary>
        public bool    CreateZip         { get; set; } = true;
        /// <summary>INDEX.md（一覧マークダウン）を生成するか</summary>
        public bool    CreateIndex       { get; set; } = true;
        /// <summary>コピーではなく移動するか（通常はコピー）</summary>
        public bool    MoveFiles         { get; set; } = false;
        /// <summary>対象拡張子フィルター（空=全ファイル）例: ".pdf,.docx"</summary>
        public string  ExtensionFilter   { get; set; } = "";
    }

    // =====================================================
    // 集約実行結果
    // =====================================================
    public class CollectResult
    {
        public bool     Success          { get; set; }
        public string   OutputPath       { get; set; } = "";
        public string?  ZipPath          { get; set; }
        public string?  IndexPath        { get; set; }
        public int      TotalFiles       { get; set; }
        public long     TotalBytes       { get; set; }
        public List<string> Errors       { get; set; } = new();
        public List<FileEntry> Files     { get; set; } = new();
    }

    public class FileEntry
    {
        public string CategoryName { get; set; } = "";
        public string TaskName     { get; set; } = "";
        public string FileName     { get; set; } = "";
        public string DestPath     { get; set; } = "";
        public long   SizeBytes    { get; set; }
    }

    // =====================================================
    // メイン: 成果物を集約する
    // =====================================================
    public CollectResult Collect(CollectOptions options)
    {
        var result = new CollectResult();
        if (_projectService.CurrentProject == null)
        {
            result.Errors.Add("プロジェクトが読み込まれていません");
            return result;
        }

        var project = _projectService.CurrentProject;

        // 出力先フォルダを決定
        var baseOut = options.OutputBasePath ?? project.Settings.ProjectPath;
        var folderName = $"{options.OutputFolderName}_{DateTime.Now:yyyyMMdd_HHmm}";
        var outputPath = Path.Combine(baseOut, folderName);
        Directory.CreateDirectory(outputPath);
        result.OutputPath = outputPath;

        // 拡張子フィルター
        var extFilter = options.ExtensionFilter
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLower())
            .Where(e => !string.IsNullOrEmpty(e))
            .ToHashSet();

        // カテゴリーマップ
        var catMap = project.Categories.ToDictionary(c => c.Id, c => c);

        // 対象タスク
        var tasks = project.Tasks
            .Where(t => options.FilterCategoryId == null || t.CategoryId == options.FilterCategoryId)
            .Where(t => !options.CompletedOnly   || t.Status == "完了")
            .ToList();

        foreach (var task in tasks)
        {
            if (!task.FolderCreated || !Directory.Exists(task.FolderPath)) continue;

            catMap.TryGetValue(task.CategoryId, out var cat);
            var catName  = cat?.Name ?? "未分類";
            var taskName = SanitizeFolderName(task.Name);

            // 出力先サブフォルダ
            string destDir;
            if (options.GroupByCategory)
            {
                var catDir = Path.Combine(outputPath, SanitizeFolderName(catName));
                Directory.CreateDirectory(catDir);
                destDir = Path.Combine(catDir, $"{task.Id}_{taskName}");
            }
            else
            {
                destDir = Path.Combine(outputPath, $"{task.Id}_{taskName}");
            }
            Directory.CreateDirectory(destDir);

            // ファイルをコピー（またはムーブ）
            foreach (var file in GetAllFiles(task.FolderPath))
            {
                var ext = Path.GetExtension(file).ToLower();
                if (extFilter.Count > 0 && !extFilter.Contains(ext)) continue;

                var relPath  = Path.GetRelativePath(task.FolderPath, file);
                var destFile = Path.Combine(destDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                try
                {
                    if (options.MoveFiles)
                        File.Move(file, destFile, overwrite: true);
                    else
                        File.Copy(file, destFile, overwrite: true);

                    var info = new FileInfo(destFile);
                    result.Files.Add(new FileEntry
                    {
                        CategoryName = catName,
                        TaskName     = task.Name,
                        FileName     = relPath,
                        DestPath     = destFile,
                        SizeBytes    = info.Length
                    });
                    result.TotalFiles++;
                    result.TotalBytes += info.Length;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"コピー失敗: {file} → {ex.Message}");
                }
            }
        }

        // INDEX.md 生成
        if (options.CreateIndex)
        {
            var indexPath = Path.Combine(outputPath, "INDEX.md");
            WriteIndex(indexPath, project, tasks, result, options);
            result.IndexPath = indexPath;
        }

        // ZIP 生成
        if (options.CreateZip && result.TotalFiles > 0)
        {
            var zipPath = outputPath + ".zip";
            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(outputPath, zipPath);
                result.ZipPath = zipPath;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"ZIP生成失敗: {ex.Message}");
            }
        }

        result.Success = true;
        return result;
    }

    // =====================================================
    // INDEX.md 生成
    // =====================================================
    private static void WriteIndex(string path, ProjectData project,
        List<TaskItem> tasks, CollectResult result, CollectOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {project.Settings.ProjectName} — 成果物インデックス");
        sb.AppendLine();
        sb.AppendLine($"**生成日時:** {DateTime.Now:yyyy/MM/dd HH:mm}  ");
        sb.AppendLine($"**対象:** {(options.CompletedOnly ? "完了タスクのみ" : "全タスク")}  ");
        sb.AppendLine($"**ファイル数:** {result.TotalFiles}件  ");
        sb.AppendLine($"**合計サイズ:** {FormatBytes(result.TotalBytes)}  ");
        if (!string.IsNullOrEmpty(options.ExtensionFilter))
            sb.AppendLine($"**拡張子フィルター:** {options.ExtensionFilter}  ");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // カテゴリー別サマリー
        var byCategory = result.Files.GroupBy(f => f.CategoryName);
        foreach (var group in byCategory)
        {
            sb.AppendLine($"## 📂 {group.Key}");
            sb.AppendLine();
            var byTask = group.GroupBy(f => f.TaskName);
            foreach (var taskGroup in byTask)
            {
                sb.AppendLine($"### ✅ {taskGroup.Key}");
                foreach (var file in taskGroup)
                {
                    sb.AppendLine($"- `{file.FileName}` ({FormatBytes(file.SizeBytes)})");
                }
                sb.AppendLine();
            }
        }

        // タスク完了状況サマリー
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 📋 タスク一覧");
        sb.AppendLine();
        sb.AppendLine("| ID | タスク名 | 担当者 | ステータス | 予定終了 | 実績終了 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var task in tasks.OrderBy(t => t.CategoryId).ThenBy(t => t.Id))
        {
            sb.AppendLine($"| {task.Id} | {task.Name} | {task.Assignee} | {task.Status} " +
                $"| {task.PlannedEndDate:yyyy/MM/dd} | {task.ActualEndDate:yyyy/MM/dd} |");
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    // =====================================================
    // 完了チェック
    // =====================================================
    public (bool allDone, int done, int total, List<TaskItem> incomplete) CheckCompletion(
        string? categoryId = null)
    {
        if (_projectService.CurrentProject == null) return (false, 0, 0, new());
        var tasks = _projectService.CurrentProject.Tasks
            .Where(t => categoryId == null || t.CategoryId == categoryId)
            .ToList();
        var incomplete = tasks.Where(t => t.Status != "完了").ToList();
        return (incomplete.Count == 0, tasks.Count - incomplete.Count, tasks.Count, incomplete);
    }

    // =====================================================
    // ヘルパー
    // =====================================================
    private static IEnumerable<string> GetAllFiles(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            yield return file;
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return s.Length > 40 ? s[..40] : s;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024         => $"{bytes} B",
        < 1024 * 1024  => $"{bytes / 1024.0:F1} KB",
        _              => $"{bytes / (1024.0 * 1024):F1} MB"
    };
}

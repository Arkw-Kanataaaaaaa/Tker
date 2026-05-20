using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace TKer.Services;

/// <summary>
/// 環境構築キックファイル (setup.pf.json) の生成・読み込みサービス
/// キックファイルが置かれたフォルダ配下に初期構築を実行する
/// </summary>
public class SetupService
{
    private const string KickFileName = "setup.pf.json";
    private const string CompletedFolder = "作業完了";
    private const string TemplateFolder = "_templates";
    private const string DocsFolder = "_docs";

    public class KickConfig
    {
        public string ProjectName { get; set; } = "NewProject";
        public string Description { get; set; } = "";
        public string[] DefaultCategories { get; set; } = Array.Empty<string>();
        public bool CreateDefaultFolders { get; set; } = true;
        public string Version { get; set; } = "1.0";
        public string CreatedBy { get; set; } = "";
    }

    private readonly ProjectService _projectService;

    public SetupService(ProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>
    /// キックファイルを指定フォルダに生成する
    /// </summary>
    public string GenerateKickFile(string targetDirectory, string projectName, string description = "")
    {
        Directory.CreateDirectory(targetDirectory);
        var config = new KickConfig
        {
            ProjectName = projectName,
            Description = description,
            DefaultCategories = new[] { "基本設計", "詳細設計", "開発", "テスト", "移行" },
            CreateDefaultFolders = true,
            Version = "1.0",
            CreatedBy = Environment.UserName
        };

        var kickPath = Path.Combine(targetDirectory, KickFileName);
        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        File.WriteAllText(kickPath, json);
        return kickPath;
    }

    /// <summary>
    /// キックファイルを読み込んで環境構築を実行する
    /// </summary>
    public bool ExecuteKickFile(string kickFilePath, out string resultMessage)
    {
        resultMessage = "";
        try
        {
            if (!File.Exists(kickFilePath))
            {
                resultMessage = $"キックファイルが見つかりません: {kickFilePath}";
                return false;
            }

            var json = File.ReadAllText(kickFilePath);
            var config = JsonConvert.DeserializeObject<KickConfig>(json);
            if (config == null)
            {
                resultMessage = "キックファイルの解析に失敗しました";
                return false;
            }

            var baseDir = Path.GetDirectoryName(kickFilePath)!;

            // プロジェクト作成
            _projectService.CreateProject(baseDir, config.ProjectName, config.Description);

            // デフォルトフォルダ作成
            if (config.CreateDefaultFolders)
            {
                CreateDefaultStructure(config);
            }

            // デフォルトカテゴリー作成
            foreach (var catName in config.DefaultCategories)
            {
                if (!string.IsNullOrWhiteSpace(catName))
                    _projectService.AddCategory(catName);
            }

            // キックファイルをREADMEと共に保存
            WriteReadme(config);

            resultMessage = $"プロジェクト「{config.ProjectName}」の環境構築が完了しました。\n"
                          + $"場所: {_projectService.CurrentProject?.Settings.ProjectPath}";
            return true;
        }
        catch (Exception ex)
        {
            resultMessage = $"環境構築中にエラーが発生しました: {ex.Message}";
            return false;
        }
    }

    private void CreateDefaultStructure(KickConfig config)
    {
        if (_projectService.CurrentProject == null) return;
        var projectPath = _projectService.CurrentProject.Settings.ProjectPath;

        // 標準フォルダ
        Directory.CreateDirectory(Path.Combine(projectPath, CompletedFolder));
        Directory.CreateDirectory(Path.Combine(projectPath, TemplateFolder));
        Directory.CreateDirectory(Path.Combine(projectPath, DocsFolder));
    }

    private void WriteReadme(KickConfig config)
    {
        if (_projectService.CurrentProject == null) return;
        var projectPath = _projectService.CurrentProject.Settings.ProjectPath;
        var readmePath = Path.Combine(projectPath, "README.md");

        var content = $"""
            # {config.ProjectName}

            {config.Description}

            ## プロジェクト情報
            - 作成日: {DateTime.Now:yyyy-MM-dd HH:mm}
            - 作成者: {config.CreatedBy}
            - バージョン: {config.Version}

            ## フォルダ構成
            ```
            {config.ProjectName}/
            ├── 作業完了/       # 完了タスクフォルダの移動先
            ├── _templates/    # テンプレートファイル
            ├── _docs/         # プロジェクトドキュメント
            ├── [カテゴリID]_[カテゴリ名]/   # 各カテゴリフォルダ
            │   └── [タスクID]_[タスク名]/   # 各タスクフォルダ
            └── project_data.json  # プロジェクトデータ
            ```

            ## TKer で管理
            このプロジェクトは TKer で管理されています。
            `project_data.json` を TKer で開いてください。
            """;

        File.WriteAllText(readmePath, content);
    }

    /// <summary>
    /// キックファイルが存在するかチェック
    /// </summary>
    public static bool HasKickFile(string directory)
        => File.Exists(Path.Combine(directory, KickFileName));

    /// <summary>
    /// キックファイルのパスを返す
    /// </summary>
    public static string GetKickFilePath(string directory)
        => Path.Combine(directory, KickFileName);
}

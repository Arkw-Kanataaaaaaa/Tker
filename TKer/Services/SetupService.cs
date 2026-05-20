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
    private const string KICK_FILE_NAME    = "setup.pf.json";
    private const string COMPLETED_FOLDER  = "作業完了";
    private const string TEMPLATE_FOLDER   = "_templates";
    private const string DOCS_FOLDER       = "_docs";

    /// <summary>キックファイルの設定内容を表すクラス。</summary>
    public class KickConfig
    {
        /// <summary>プロジェクト名。</summary>
        public string ProjectName { get; set; } = "NewProject";
        /// <summary>プロジェクトの説明。</summary>
        public string Description { get; set; } = "";
        /// <summary>デフォルトカテゴリー一覧。</summary>
        public string[] DefaultCategories { get; set; } = Array.Empty<string>();
        /// <summary>デフォルトフォルダを作成するかどうか。</summary>
        public bool CreateDefaultFolders { get; set; } = true;
        /// <summary>設定ファイルのバージョン。</summary>
        public string Version { get; set; } = "1.0";
        /// <summary>作成者名。</summary>
        public string CreatedBy { get; set; } = "";
    }

    private readonly ProjectService _projectService;

    /// <summary>ProjectService を受け取って初期化する。</summary>
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

        var kickPath = Path.Combine(targetDirectory, KICK_FILE_NAME);
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

    /// <summary>プロジェクトの標準フォルダ構成を作成する。</summary>
    private void CreateDefaultStructure(KickConfig config)
    {
        if (_projectService.CurrentProject == null) return;
        var projectPath = _projectService.CurrentProject.Settings.ProjectPath;

        // 標準フォルダ
        Directory.CreateDirectory(Path.Combine(projectPath, COMPLETED_FOLDER));
        Directory.CreateDirectory(Path.Combine(projectPath, TEMPLATE_FOLDER));
        Directory.CreateDirectory(Path.Combine(projectPath, DOCS_FOLDER));
    }

    /// <summary>プロジェクトフォルダに README.md を生成する。</summary>
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
        => File.Exists(Path.Combine(directory, KICK_FILE_NAME));

    /// <summary>
    /// キックファイルのパスを返す
    /// </summary>
    public static string GetKickFilePath(string directory)
        => Path.Combine(directory, KICK_FILE_NAME);
}

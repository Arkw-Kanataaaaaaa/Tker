using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// アプリ全体設定（複数プロジェクト一覧）を管理するサービス。
/// %AppData%\TKer\settings.json に保存される。
/// </summary>
public class AppSettingsService
{
    private static readonly string SETTINGS_DIR =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TKer");
    private static readonly string SETTINGS_FILE =
        Path.Combine(SETTINGS_DIR, "settings.json");

    private AppSettings _settings;

    // ⑩ サマリーキャッシュ（30秒有効）
    private List<ProjectSummary>? _summaryCache;
    private DateTime _summaryCachedAt;
    private static readonly TimeSpan SUMMARY_CACHE_TTL = TimeSpan.FromSeconds(30);


    /// <summary>設定ファイルを読み込んでサービスを初期化する。</summary>
    public AppSettingsService()
    {
        _settings = Load();
    }

    /// <summary>内部設定オブジェクトへの直接アクセス（カスタムプリセット等）</summary>
    public AppSettings Settings => _settings;

    /// <summary>最終オープン日時の降順・ピン留め優先で並べた最近のプロジェクト一覧を返す。</summary>
    public IReadOnlyList<ProjectEntry> RecentProjects =>
        _settings.RecentProjects
            .OrderByDescending(p => p.IsPinned)
            .ThenByDescending(p => p.LastOpened)
            .ToList();

    // ── 登録 / 更新 ───────────────────────────────
    /// <summary>プロジェクトを最近のプロジェクト一覧に登録または更新して保存する。</summary>
    public void RegisterProject(string dataFilePath, string projectName,
                                string projectPath, string description = "")
    {
        var existing = _settings.RecentProjects
            .FirstOrDefault(p => p.DataFilePath == dataFilePath);

        if (existing != null)
        {
            existing.LastOpened = DateTime.Now;
            existing.ProjectName = projectName;
            existing.Description = description;
        }
        else
        {
            _settings.RecentProjects.Add(new ProjectEntry
            {
                DataFilePath = dataFilePath,
                ProjectName = projectName,
                ProjectPath = projectPath,
                Description = description,
                LastOpened = DateTime.Now
            });
        }

        _settings.LastOpenedProjectPath = dataFilePath;
        TrimRecentProjects();
        _summaryCache = null; // キャッシュクリア
        Save();
    }

    // ── コレクションファイルパス管理 ─────────────────
    /// <summary>コレクションのファイルパスを登録する（重複は無視）。</summary>
    public void AddCollectionFilePath(string path)
    {
        if (!_settings.CollectionFilePaths.Contains(path))
            _settings.CollectionFilePaths.Add(path);
        Save();
    }

    /// <summary>コレクションのファイルパスを削除して保存する。</summary>
    public void RemoveCollectionFilePath(string path)
    {
        _settings.CollectionFilePaths.Remove(path);
        Save();
    }

    /// <summary>コレクションのファイルパス一覧を置き換えて保存する。</summary>
    public void SyncCollectionFilePaths(IEnumerable<string> paths)
    {
        _settings.CollectionFilePaths = paths.ToList();
        Save();
    }

    /// <summary>登録済みコレクションファイルパスの一覧を返す。</summary>
    public IReadOnlyList<string> CollectionFilePaths => _settings.CollectionFilePaths;

    /// <summary>指定パスのプロジェクトを最近の一覧から削除して保存する。</summary>
    public void RemoveProject(string dataFilePath)
    {
        _settings.RecentProjects.RemoveAll(p => p.DataFilePath == dataFilePath);
        Save();
    }

    /// <summary>指定プロジェクトのピン留め状態を切り替えて保存する。</summary>
    public void TogglePin(string dataFilePath)
    {
        var entry = _settings.RecentProjects.FirstOrDefault(p => p.DataFilePath == dataFilePath);
        if (entry != null) { entry.IsPinned = !entry.IsPinned; Save(); }
    }

    public string? LastOpenedProjectPath => _settings.LastOpenedProjectPath;


    // ── アプリ設定の更新 ─────────────────────────
    /// <summary>アラート設定（猶予日数・完了タスク表示）を更新して保存する。</summary>
    public void UpdateAlertSettings(int graceDays, bool showCompletedInSchedule)
    {
        _settings.AlertNotStartedGraceDays = graceDays;
        _settings.ShowCompletedInSchedule = showCompletedInSchedule;
        Save();
    }

    /// <summary>外観設定（背景・モード・行透過・ガント色など）を更新して保存する。</summary>
    public void UpdateAppearanceSettings(string? backgroundImagePath, string appMode,
        double taskRowOpacity = 1.0, string ganttRowColor = "#EBEBEB", double ganttRowOpacity = 1.0,
        string rowBorderColor = "#606060")
    {
        _settings.BackgroundImagePath = backgroundImagePath;
        _settings.AppMode             = appMode;
        _settings.TaskRowOpacity      = Math.Clamp(taskRowOpacity, 0.1, 1.0);
        _settings.GanttRowColor       = ganttRowColor;
        _settings.GanttRowOpacity     = Math.Clamp(ganttRowOpacity, 0.0, 1.0);
        _settings.RowBorderColor      = rowBorderColor;
        Save();
    }

    public int AlertNotStartedGraceDays => _settings.AlertNotStartedGraceDays;
    public bool ShowCompletedInSchedule => _settings.ShowCompletedInSchedule;
    public string? BackgroundImagePath  => _settings.BackgroundImagePath;
    public string AppMode               => _settings.AppMode;
    public double TaskRowOpacity        => _settings.TaskRowOpacity;
    public string GanttRowColor         => _settings.GanttRowColor;
    public double GanttRowOpacity       => _settings.GanttRowOpacity;
    public string RowBorderColor        => _settings.RowBorderColor;
    public string ProjectListBgColor    => _settings.ProjectListBgColor;
    public double ProjectListBgOpacity  => _settings.ProjectListBgOpacity;

    /// <summary>プロジェクト一覧の背景色・不透明度を更新して保存する。</summary>
    public void UpdateProjectListBackground(string bgColor, double bgOpacity)
    {
        _settings.ProjectListBgColor   = bgColor;
        _settings.ProjectListBgOpacity = Math.Clamp(bgOpacity, 0.0, 1.0);
        Save();
    }

    public string CategoryListBgColor   => _settings.CategoryListBgColor;
    public double CategoryListBgOpacity => _settings.CategoryListBgOpacity;

    /// <summary>カテゴリー一覧の背景色・不透明度を更新して保存する。</summary>
    public void UpdateCategoryListBackground(string bgColor, double bgOpacity)
    {
        _settings.CategoryListBgColor   = bgColor;
        _settings.CategoryListBgOpacity = Math.Clamp(bgOpacity, 0.0, 1.0);
        Save();
    }

    public string AppSettingsBgColor    => _settings.AppSettingsBgColor;
    public double AppSettingsBgOpacity  => _settings.AppSettingsBgOpacity;

    /// <summary>アプリ設定画面の背景色・不透明度を更新して保存する。</summary>
    public void UpdateAppSettingsBackground(string bgColor, double bgOpacity)
    {
        _settings.AppSettingsBgColor   = bgColor;
        _settings.AppSettingsBgOpacity = Math.Clamp(bgOpacity, 0.0, 1.0);
        Save();
    }
    public IReadOnlyList<AppShortcut> Shortcuts => _settings.Shortcuts;

    /// <summary>ショートカット一覧を保存する。</summary>
    public void SaveShortcuts(List<AppShortcut> shortcuts)
    {
        _settings.Shortcuts = shortcuts;
        Save();
    }

    public IReadOnlyList<string> MenuOrder => _settings.MenuOrder;
    /// <summary>メニュー表示順を保存する。</summary>
    public void SaveMenuOrder(List<string> order)
    {
        _settings.MenuOrder = order;
        Save();
    }

    public ThemeColors Theme => _settings.Theme;
    /// <summary>テーマカラーを保存する。</summary>
    public void SaveTheme(ThemeColors theme)
    {
        _settings.Theme = theme;
        Save();
    }

    public bool SkipRestartConfirm => _settings.SkipRestartConfirm;
    /// <summary>再起動確認ダイアログのスキップ設定を保存する。</summary>
    public void SetSkipRestartConfirm(bool skip)
    {
        _settings.SkipRestartConfirm = skip;
        Save();
    }

    public PomodoroSettings PomodoroSettings => _settings.Pomodoro;
    /// <summary>ポモドーロ設定を保存する。</summary>
    public void SavePomodoro(PomodoroSettings p)
    {
        _settings.Pomodoro = p;
        Save();
    }

    public List<CategoryPreset> CategoryPresets => _settings.CategoryPresets;
    /// <summary>カテゴリープリセット一覧を保存する。</summary>
    public void SaveCategoryPresets(List<CategoryPreset> presets)
    {
        _settings.CategoryPresets = presets;
        Save();
    }

    // ── カレンダーウィジェット ─────────────────────
    public CalendarWidgetSettings WidgetSettings => _settings.CalendarWidget;
    /// <summary>カレンダーウィジェット設定を保存する。</summary>
    public void SaveWidgetSettings(CalendarWidgetSettings ws)
    {
        _settings.CalendarWidget = ws;
        Save();
    }

    // ── 栞ウィジェット ─────────────────────────────
    public BookmarkWidgetSettings BookmarkWidgetSettings => _settings.BookmarkWidget;
    /// <summary>栞ウィジェット設定を保存する。</summary>
    public void SaveBookmarkSettings(BookmarkWidgetSettings bs)
    {
        _settings.BookmarkWidget = bs;
        Save();
    }

    // ── ログローテーション ────────────────────────
    public LogRotationSettings LogRotation => _settings.LogRotation;
    /// <summary>ログローテーション設定を保存する。</summary>
    public void SaveLogRotation(LogRotationSettings lr)
    {
        _settings.LogRotation = lr;
        Save();
    }

    // ── テーマプリセット ─────────────────────────
    private static readonly List<ThemePreset> BUILT_IN_PRESETS = new()
    {
        new() {
            Id = "builtin_dark", Name = "ダーク（デフォルト）", IsBuiltIn = true,
            Theme = new ThemeColors()
        },
        new() {
            Id = "builtin_deepblue", Name = "ディープブルー", IsBuiltIn = true,
            Theme = new ThemeColors
            {
                TextPrimary = "#E0E8FF", AccentCyan = "#4DA6FF",
                BgSecondary = "#0D1B2A", BgCard = "#1A2B3C", Border = "#1E3A5F",
                ButtonBg = "#0F2A45", ButtonBorder = "#1E4A70", DropdownBg = "#122030"
            }
        },
        new() {
            Id = "builtin_forest", Name = "フォレスト", IsBuiltIn = true,
            Theme = new ThemeColors
            {
                AccentCyan = "#66BB6A",
                BgSecondary = "#0F1F0F", BgCard = "#1A2E1A", Border = "#2A4A2A",
                ButtonBg = "#1A3A1A", ButtonBorder = "#2A5A2A", DropdownBg = "#122012"
            }
        },
        new() {
            Id = "builtin_sunset", Name = "サンセット", IsBuiltIn = true,
            Theme = new ThemeColors
            {
                TextPrimary = "#FFE0CC", AccentCyan = "#FF7043",
                BgSecondary = "#200F08", BgCard = "#2A1510", Border = "#4A2A20",
                ButtonBg = "#3A1A10", ButtonBorder = "#5A2A18", DropdownBg = "#201008"
            }
        },
        new() {
            Id = "builtin_purple", Name = "パープル", IsBuiltIn = true,
            Theme = new ThemeColors
            {
                TextPrimary = "#E8E0FF", AccentCyan = "#9C5CFF",
                BgSecondary = "#120A1E", BgCard = "#1E1030", Border = "#3A2060",
                ButtonBg = "#2A1050", ButtonBorder = "#4A2080", DropdownBg = "#100818"
            }
        },
        new() {
            Id = "builtin_mono", Name = "モノクローム", IsBuiltIn = true,
            Theme = new ThemeColors
            {
                TextPrimary = "#FFFFFF", TextSecond = "#BBBBBB", AccentCyan = "#AAAAAA",
                BgSecondary = "#1A1A1A", BgCard = "#242424", Border = "#404040",
                ButtonBg = "#303030", ButtonBorder = "#505050", DropdownBg = "#1E1E1E"
            }
        },
    };

    /// <summary>組み込みプリセットとユーザープリセットを結合して返す。</summary>
    public IReadOnlyList<ThemePreset> GetAllPresets()
        => BUILT_IN_PRESETS.Concat(_settings.UserPresets).ToList();

    /// <summary>指定プリセットのテーマとセクションテーマを適用して保存する。</summary>
    public void ApplyPreset(ThemePreset preset)
    {
        _settings.Theme        = CloneTheme(preset.Theme);
        _settings.SectionThemes = new Dictionary<string, SectionTheme>(preset.SectionThemes);
        Save();
    }

    /// <summary>現在のテーマ設定から新規プリセットを作成する。</summary>
    public ThemePreset CreatePresetFromCurrent(string name) => new()
    {
        Name          = name,
        IsBuiltIn     = false,
        Theme         = CloneTheme(_settings.Theme),
        SectionThemes = new Dictionary<string, SectionTheme>(_settings.SectionThemes)
    };

    /// <summary>ユーザープリセットを追加して保存する。</summary>
    public void SaveUserPreset(ThemePreset preset)
    {
        _settings.UserPresets.Add(preset);
        Save();
    }

    /// <summary>指定IDのユーザープリセットを削除して保存する。</summary>
    public void DeleteUserPreset(string id)
    {
        _settings.UserPresets.RemoveAll(p => p.Id == id);
        Save();
    }

    /// <summary>ThemeColorsオブジェクトをディープコピーして返す。</summary>
    private static ThemeColors CloneTheme(ThemeColors t) => new()
    {
        TextPrimary  = t.TextPrimary,
        TextSecond   = t.TextSecond,
        AccentCyan   = t.AccentCyan,
        BgSecondary  = t.BgSecondary,
        BgCard       = t.BgCard,
        Border       = t.Border,
        BgCardOpacity= t.BgCardOpacity,
        FontFamily   = t.FontFamily,
        ButtonBg     = t.ButtonBg,
        ButtonBorder = t.ButtonBorder,
        DropdownBg   = t.DropdownBg,
    };

    // ── ホームレイアウト ─────────────────────────────
    private static readonly (string Id, int Row, int Col, int RowSpan, int ColSpan)[] DEFAULT_HOME_SLOTS =
    [
        ("Header",     0, 0, 1, 2),
        ("Alert",      1, 0, 1, 2),
        ("RecentTask", 2, 0, 1, 1),
        ("Shortcut",   2, 1, 1, 1),
        ("Project",    3, 0, 1, 1),
        ("Calendar",   3, 1, 1, 1),
        ("QuickNav",   4, 0, 1, 1),
        ("Version",    4, 1, 1, 1),
    ];
    private static readonly double[] DEFAULT_COLUMN_WIDTHS = { -1, -1 };
    private static readonly double[] DEFAULT_ROW_HEIGHTS   = { 80, 160, 220, 280, 130 };

    /// <summary>
    /// 保存済みスロットが新仕様（Row/Column 設定済み）かを判定。
    /// すべて Row==0 && Column==0 && RowSpan==1 && ColumnSpan==1 の場合は旧仕様とみなす。
    /// </summary>
    private static bool IsSavedLayoutValid(Dictionary<string, HomeLayoutSlot> saved)
    {
        if (saved.Count == 0) return false;
        foreach (var s in saved.Values)
        {
            if (s.Row != 0 || s.Column != 0 || s.RowSpan > 1 || s.ColumnSpan > 1)
                return true;
        }
        return false;
    }

    /// <summary>保存済みレイアウトが有効な場合はそれを、そうでなければデフォルトを返す。</summary>
    public List<HomeLayoutSlot> GetEffectiveHomeLayout()
    {
        bool useSaved = IsSavedLayoutValid(_settings.HomeLayout);
        return DEFAULT_HOME_SLOTS.Select(d =>
        {
            if (useSaved && _settings.HomeLayout.TryGetValue(d.Id, out var saved))
            {
                if (string.IsNullOrEmpty(saved.ComponentId)) saved.ComponentId = d.Id;
                if (saved.RowSpan    < 1) saved.RowSpan    = 1;
                if (saved.ColumnSpan < 1) saved.ColumnSpan = 1;
                return saved;
            }
            return new HomeLayoutSlot
            {
                ComponentId = d.Id,
                Row         = d.Row,
                Column      = d.Col,
                RowSpan     = d.RowSpan,
                ColumnSpan  = d.ColSpan,
            };
        }).ToList();
    }

    /// <summary>保存済みカラム幅が存在すればそれを、なければデフォルトを返す。</summary>
    public List<double> GetEffectiveColumnWidths()
    {
        if (_settings.HomeColumnWidths != null && _settings.HomeColumnWidths.Count > 0)
            return _settings.HomeColumnWidths.ToList();
        return DEFAULT_COLUMN_WIDTHS.ToList();
    }

    /// <summary>保存済み行高さが存在すればそれを、なければデフォルトを返す。</summary>
    public List<double> GetEffectiveRowHeights()
    {
        if (_settings.HomeRowHeights != null && _settings.HomeRowHeights.Count > 0)
            return _settings.HomeRowHeights.ToList();
        return DEFAULT_ROW_HEIGHTS.ToList();
    }

    /// <summary>ホーム画面のレイアウト設定を保存する。</summary>
    public void SaveHomeLayout(Dictionary<string, HomeLayoutSlot> layout)
    {
        _settings.HomeLayout = layout;
        Save();
    }

    /// <summary>ホーム画面のカラム幅と行高さを保存する。</summary>
    public void SaveHomeLanes(List<double> cols, List<double> rows)
    {
        _settings.HomeColumnWidths = cols ?? new List<double>();
        _settings.HomeRowHeights   = rows ?? new List<double>();
        Save();
    }

    public IReadOnlyDictionary<string, SectionTheme> SectionThemes => _settings.SectionThemes;
    /// <summary>指定キーのセクションテーマを返す（未登録時は既定値）。</summary>
    public SectionTheme GetSectionTheme(string key)
        => _settings.SectionThemes.TryGetValue(key, out var t) ? t : new SectionTheme();
    /// <summary>指定セクションテーマを既存設定にマージして保存する。</summary>
    public void SaveSectionThemes(Dictionary<string, SectionTheme> themes)
    {
        var merged = new Dictionary<string, SectionTheme>(_settings.SectionThemes);
        foreach (var kv in themes) merged[kv.Key] = kv.Value;
        _settings.SectionThemes = merged;
        Save();
    }

    /// <summary>指定キーのセクションテーマを更新して保存する。</summary>
    public void UpdateSectionTheme(string key, SectionTheme theme)
    {
        var dict = new Dictionary<string, SectionTheme>(_settings.SectionThemes) { [key] = theme };
        _settings.SectionThemes = dict;
        Save();
    }

    /// <summary>全セクションテーマをリセットして保存する。</summary>
    public void ResetAllSectionThemes()
    {
        _settings.SectionThemes = new();
        Save();
    }

    // ⑲ 最近使ったプロジェクト上限管理（最大20件、ピン留め優先）
    /// <summary>最近使ったプロジェクトを最大20件に切り詰め、ピン留めを優先する。</summary>
    private void TrimRecentProjects()
    {
        const int MaxRecent = 20;
        var pinned = _settings.RecentProjects.Where(p => p.IsPinned).ToList();
        var unpinned = _settings.RecentProjects.Where(p => !p.IsPinned)
            .OrderByDescending(p => p.LastOpened).ToList();
        var kept = pinned.Concat(unpinned).Take(MaxRecent).ToList();
        _settings.RecentProjects = kept;
    }

    // ── ロード / セーブ ───────────────────────────
    /// <summary>settings.json をディスクから再読み込みし、サマリーキャッシュをクリアする。</summary>
    public void Reload()
    {
        _settings     = Load();
        _summaryCache = null;
    }

    /// <summary>設定ファイルを読み込んで返す（ファイル不在・破損時は新規設定を返す）。</summary>
    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SETTINGS_FILE))
            {
                var json = File.ReadAllText(SETTINGS_FILE);
                var loaded = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                MigrateMenuOrder(loaded);
                return loaded;
            }
        }
        catch { /* 初回起動や破損時は新規作成 */ }
        return new AppSettings();
    }

    /// <summary>
    /// 旧バージョンのメニュータグを新タグに変換し、廃止タグを除去する。
    /// 旧: "プロジェクト" → 新: "ライブラリ"
    /// 廃止: "スケジュール", "データ" を除外
    /// </summary>
    private static void MigrateMenuOrder(AppSettings s)
    {
        if (s.MenuOrder == null || s.MenuOrder.Count == 0) return;
        var migrated = new List<string>();
        foreach (var tag in s.MenuOrder)
        {
            if (tag == "プロジェクト") migrated.Add("ライブラリ");
            else if (tag == "スケジュール" || tag == "データ") continue;
            else migrated.Add(tag);
        }
        // 重複除去（順序保持）
        s.MenuOrder = migrated.Distinct().ToList();
    }

    /// <summary>現在の設定をJSONファイルに書き出す。</summary>
    private void Save()
    {
        Directory.CreateDirectory(SETTINGS_DIR);
        var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
        File.WriteAllText(SETTINGS_FILE, json);
    }

    // ── アラート集計（全プロジェクト横断） ─────────
    /// <summary>
    /// 登録済みプロジェクトを全件ロードしてアラート一覧を返す。
    /// ファイルが存在しないエントリは自動除外。
    /// </summary>
    public List<AlertItem> CollectAlerts()
    {
        var alerts = new List<AlertItem>();
        var dead = new List<string>();

        foreach (var entry in _settings.RecentProjects)
        {
            if (!File.Exists(entry.DataFilePath)) { dead.Add(entry.DataFilePath); continue; }

            try
            {
                var json = File.ReadAllText(entry.DataFilePath);
                var project = JsonConvert.DeserializeObject<ProjectData>(json);
                if (project == null) continue;

                foreach (var task in project.Tasks)
                {
                    AlertLevel? level = null;
                    if (task.IsOverdue) level = AlertLevel.Overdue;
                    else if (task.IsDueSoon) level = AlertLevel.DueSoon;
                    else if (task.IsNotStartedOverdue(_settings.AlertNotStartedGraceDays)) level = AlertLevel.NotStarted;

                    if (level.HasValue)
                    {
                        alerts.Add(new AlertItem
                        {
                            ProjectName = entry.ProjectName,
                            TaskId = task.Id,
                            TaskName = task.Name,
                            Assignee = task.Assignee,
                            Level = level.Value,
                            PlannedEndDate = task.PlannedEndDate,
                            RemainingDays = task.RemainingDays,
                            Source = task,
                            DataFilePath = entry.DataFilePath
                        });
                    }
                }
            }
            catch { /* 読み込み失敗は無視 */ }
        }

        // 壊れたエントリを除去
        foreach (var d in dead) _settings.RecentProjects.RemoveAll(p => p.DataFilePath == d);
        if (dead.Count > 0) Save();

        return alerts
            .OrderBy(a => a.Level)
            .ThenBy(a => a.RemainingDays ?? int.MaxValue)
            .ToList();
    }

    /// <summary>全プロジェクトのサマリーを返す（30秒キャッシュ）</summary>
    public List<ProjectSummary> CollectSummaries(bool forceRefresh = false)
    {
        if (!forceRefresh && _summaryCache != null &&
            (DateTime.Now - _summaryCachedAt) < SUMMARY_CACHE_TTL)
            return _summaryCache;

        {
            var result = new List<ProjectSummary>();
            foreach (var entry in RecentProjects)
            {
                if (!File.Exists(entry.DataFilePath)) continue;
                try
                {
                    var json = File.ReadAllText(entry.DataFilePath);
                    var project = JsonConvert.DeserializeObject<ProjectData>(json);
                    if (project == null) continue;

                    result.Add(new ProjectSummary
                    {
                        Entry = entry,
                        TotalTasks = project.Tasks.Count,
                        DoneTasks = project.Tasks.Count(t => t.Status == "完了"),
                        WipTasks = project.Tasks.Count(t => t.Status == "対応中"),
                        OverdueTasks = project.Tasks.Count(t => t.IsOverdue),
                        CreatedAt = project.Settings.CreatedAt,
                        UpdatedAt = project.Settings.UpdatedAt,
                        ProjectStartDate = project.Settings.ProjectStartDate,
                        ProjectEndDate = project.Settings.ProjectEndDate,
                        UseFolderManagement = project.Settings.UseFolderManagement
                    });
                }
                catch { }
            }
            _summaryCache = result;
            _summaryCachedAt = DateTime.Now;
            return result;
        }
    }
}
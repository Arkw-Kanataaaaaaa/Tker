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
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TKer");
    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "settings.json");

    private AppSettings _settings;

    // ⑩ サマリーキャッシュ（30秒有効）
    private List<ProjectSummary>? _summaryCache;
    private DateTime _summaryCachedAt;
    private static readonly TimeSpan SummaryCacheTtl = TimeSpan.FromSeconds(30);


    public AppSettingsService()
    {
        _settings = Load();
    }

    /// <summary>内部設定オブジェクトへの直接アクセス（カスタムプリセット等）</summary>
    public AppSettings Settings => _settings;

    public IReadOnlyList<ProjectEntry> RecentProjects =>
        _settings.RecentProjects
            .OrderByDescending(p => p.IsPinned)
            .ThenByDescending(p => p.LastOpened)
            .ToList();

    // ── 登録 / 更新 ───────────────────────────────
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

    public void RemoveProject(string dataFilePath)
    {
        _settings.RecentProjects.RemoveAll(p => p.DataFilePath == dataFilePath);
        Save();
    }

    public void TogglePin(string dataFilePath)
    {
        var entry = _settings.RecentProjects.FirstOrDefault(p => p.DataFilePath == dataFilePath);
        if (entry != null) { entry.IsPinned = !entry.IsPinned; Save(); }
    }

    public string? LastOpenedProjectPath => _settings.LastOpenedProjectPath;


    // ── アプリ設定の更新 ─────────────────────────
    public void UpdateAlertSettings(int graceDays, bool showCompletedInSchedule)
    {
        _settings.AlertNotStartedGraceDays = graceDays;
        _settings.ShowCompletedInSchedule = showCompletedInSchedule;
        Save();
    }

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

    public void UpdateProjectListBackground(string bgColor, double bgOpacity)
    {
        _settings.ProjectListBgColor   = bgColor;
        _settings.ProjectListBgOpacity = Math.Clamp(bgOpacity, 0.0, 1.0);
        Save();
    }

    public string CategoryListBgColor   => _settings.CategoryListBgColor;
    public double CategoryListBgOpacity => _settings.CategoryListBgOpacity;

    public void UpdateCategoryListBackground(string bgColor, double bgOpacity)
    {
        _settings.CategoryListBgColor   = bgColor;
        _settings.CategoryListBgOpacity = Math.Clamp(bgOpacity, 0.0, 1.0);
        Save();
    }

    public string AppSettingsBgColor    => _settings.AppSettingsBgColor;
    public double AppSettingsBgOpacity  => _settings.AppSettingsBgOpacity;

    public void UpdateAppSettingsBackground(string bgColor, double bgOpacity)
    {
        _settings.AppSettingsBgColor   = bgColor;
        _settings.AppSettingsBgOpacity = Math.Clamp(bgOpacity, 0.0, 1.0);
        Save();
    }
    public IReadOnlyList<AppShortcut> Shortcuts => _settings.Shortcuts;

    public void SaveShortcuts(List<AppShortcut> shortcuts)
    {
        _settings.Shortcuts = shortcuts;
        Save();
    }

    public IReadOnlyList<string> MenuOrder => _settings.MenuOrder;
    public void SaveMenuOrder(List<string> order)
    {
        _settings.MenuOrder = order;
        Save();
    }

    public ThemeColors Theme => _settings.Theme;
    public void SaveTheme(ThemeColors theme)
    {
        _settings.Theme = theme;
        Save();
    }

    public bool SkipRestartConfirm => _settings.SkipRestartConfirm;
    public void SetSkipRestartConfirm(bool skip)
    {
        _settings.SkipRestartConfirm = skip;
        Save();
    }

    public PomodoroSettings PomodoroSettings => _settings.Pomodoro;
    public void SavePomodoro(PomodoroSettings p)
    {
        _settings.Pomodoro = p;
        Save();
    }

    public List<CategoryPreset> CategoryPresets => _settings.CategoryPresets;
    public void SaveCategoryPresets(List<CategoryPreset> presets)
    {
        _settings.CategoryPresets = presets;
        Save();
    }

    // ── カレンダーウィジェット ─────────────────────
    public CalendarWidgetSettings WidgetSettings => _settings.CalendarWidget;
    public void SaveWidgetSettings(CalendarWidgetSettings ws)
    {
        _settings.CalendarWidget = ws;
        Save();
    }

    // ── 栞ウィジェット ─────────────────────────────
    public BookmarkWidgetSettings BookmarkWidgetSettings => _settings.BookmarkWidget;
    public void SaveBookmarkSettings(BookmarkWidgetSettings bs)
    {
        _settings.BookmarkWidget = bs;
        Save();
    }

    // ── ログローテーション ────────────────────────
    public LogRotationSettings LogRotation => _settings.LogRotation;
    public void SaveLogRotation(LogRotationSettings lr)
    {
        _settings.LogRotation = lr;
        Save();
    }

    // ── テーマプリセット ─────────────────────────
    private static readonly List<ThemePreset> _builtInPresets = new()
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

    public IReadOnlyList<ThemePreset> GetAllPresets()
        => _builtInPresets.Concat(_settings.UserPresets).ToList();

    public void ApplyPreset(ThemePreset preset)
    {
        _settings.Theme        = CloneTheme(preset.Theme);
        _settings.SectionThemes = new Dictionary<string, SectionTheme>(preset.SectionThemes);
        Save();
    }

    public ThemePreset CreatePresetFromCurrent(string name) => new()
    {
        Name          = name,
        IsBuiltIn     = false,
        Theme         = CloneTheme(_settings.Theme),
        SectionThemes = new Dictionary<string, SectionTheme>(_settings.SectionThemes)
    };

    public void SaveUserPreset(ThemePreset preset)
    {
        _settings.UserPresets.Add(preset);
        Save();
    }

    public void DeleteUserPreset(string id)
    {
        _settings.UserPresets.RemoveAll(p => p.Id == id);
        Save();
    }

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
    private static readonly (string Id, int Row, int Col, int RowSpan, int ColSpan)[] DefaultHomeSlots =
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
    private static readonly double[] DefaultColumnWidths = { -1, -1 };
    private static readonly double[] DefaultRowHeights   = { 80, 160, 220, 280, 130 };

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

    public List<HomeLayoutSlot> GetEffectiveHomeLayout()
    {
        bool useSaved = IsSavedLayoutValid(_settings.HomeLayout);
        return DefaultHomeSlots.Select(d =>
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

    public List<double> GetEffectiveColumnWidths()
    {
        if (_settings.HomeColumnWidths != null && _settings.HomeColumnWidths.Count > 0)
            return _settings.HomeColumnWidths.ToList();
        return DefaultColumnWidths.ToList();
    }

    public List<double> GetEffectiveRowHeights()
    {
        if (_settings.HomeRowHeights != null && _settings.HomeRowHeights.Count > 0)
            return _settings.HomeRowHeights.ToList();
        return DefaultRowHeights.ToList();
    }

    public void SaveHomeLayout(Dictionary<string, HomeLayoutSlot> layout)
    {
        _settings.HomeLayout = layout;
        Save();
    }

    public void SaveHomeLanes(List<double> cols, List<double> rows)
    {
        _settings.HomeColumnWidths = cols ?? new List<double>();
        _settings.HomeRowHeights   = rows ?? new List<double>();
        Save();
    }

    public IReadOnlyDictionary<string, SectionTheme> SectionThemes => _settings.SectionThemes;
    public SectionTheme GetSectionTheme(string key)
        => _settings.SectionThemes.TryGetValue(key, out var t) ? t : new SectionTheme();
    public void SaveSectionThemes(Dictionary<string, SectionTheme> themes)
    {
        var merged = new Dictionary<string, SectionTheme>(_settings.SectionThemes);
        foreach (var kv in themes) merged[kv.Key] = kv.Value;
        _settings.SectionThemes = merged;
        Save();
    }

    public void UpdateSectionTheme(string key, SectionTheme theme)
    {
        var dict = new Dictionary<string, SectionTheme>(_settings.SectionThemes) { [key] = theme };
        _settings.SectionThemes = dict;
        Save();
    }

    public void ResetAllSectionThemes()
    {
        _settings.SectionThemes = new();
        Save();
    }

    // ⑲ 最近使ったプロジェクト上限管理（最大20件、ピン留め優先）
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
    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
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

    private void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
        File.WriteAllText(SettingsFile, json);
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
            (DateTime.Now - _summaryCachedAt) < SummaryCacheTtl)
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
                        CreatedAt = project.Settings.CreatedAt
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
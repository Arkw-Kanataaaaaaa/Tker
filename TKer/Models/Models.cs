using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TKer.Models;

public class AppSettings
{
    public string AppVersion { get; set; } = "1.0.0";
    public List<ProjectEntry> RecentProjects { get; set; } = new();
    public string? LastOpenedProjectPath { get; set; }
    public DateTime LastLaunched { get; set; } = DateTime.Now;
    public int AlertNotStartedGraceDays { get; set; } = 1;
    public bool ShowCompletedInSchedule { get; set; } = false;
    public string? BackgroundImagePath { get; set; }
    public string AppMode { get; set; } = "Multi";
    public List<AppShortcut> Shortcuts { get; set; } = new();
    public List<string> MenuOrder { get; set; } = new() { "ホーム", "ライブラリ", "タスク管理", "ツール", "カスタマイズ", "ヘルプ" };
    public Dictionary<string, HomeLayoutSlot> HomeLayout { get; set; } = new();
    /// <summary>ホーム画面の列幅。要素の意味: &gt;0=px, 0=Auto, &lt;0=Star（-1なら1*, -2なら2*）</summary>
    public List<double> HomeColumnWidths { get; set; } = new();
    /// <summary>ホーム画面の行高さ。同上</summary>
    public List<double> HomeRowHeights   { get; set; } = new();
    public ThemeColors Theme { get; set; } = new();
    public bool SkipRestartConfirm { get; set; } = false;
    public Dictionary<string, SectionTheme> SectionThemes { get; set; } = new();
    public double TaskRowOpacity        { get; set; } = 1.0;
    public string GanttRowColor         { get; set; } = "#EBEBEB";
    public double GanttRowOpacity       { get; set; } = 1.0;
    public string RowBorderColor        { get; set; } = "#606060";
    public string ProjectListBgColor    { get; set; } = "#2F2F2F";
    public double ProjectListBgOpacity  { get; set; } = 1.0;
    public string CategoryListBgColor   { get; set; } = "#2F2F2F";
    public double CategoryListBgOpacity { get; set; } = 1.0;
    public string AppSettingsBgColor    { get; set; } = "#2F2F2F";
    public double AppSettingsBgOpacity  { get; set; } = 1.0;
    /// <summary>ユーザーが作成したカテゴリープリセット</summary>
    public List<CategoryPreset> CategoryPresets { get; set; } = new();
    /// <summary>ポモドーロタイマー設定</summary>
    public PomodoroSettings Pomodoro { get; set; } = new();
    /// <summary>カレンダーウィジェット設定</summary>
    public CalendarWidgetSettings CalendarWidget { get; set; } = new();
    /// <summary>栞ウィジェット設定</summary>
    public BookmarkWidgetSettings BookmarkWidget { get; set; } = new();
    /// <summary>ログローテーション設定</summary>
    public LogRotationSettings LogRotation { get; set; } = new();
    /// <summary>ユーザーが作成したテーマプリセット</summary>
    public List<ThemePreset> UserPresets { get; set; } = new();
}

// ══════════════════════════════════════════════
//  カレンダーウィジェット設定
// ══════════════════════════════════════════════
public class CalendarWidgetSettings
{
    // 位置・サイズ
    public double Left   { get; set; } = 40;
    public double Top    { get; set; } = 80;
    public double Width  { get; set; } = 280;
    public double Height { get; set; } = 300;

    // 外観
    public double Opacity         { get; set; } = 0.92;
    public string BackgroundColor { get; set; } = "#E61A1F2E";
    public string AccentColor     { get; set; } = "#3D7EFF";
    public string TextColor       { get; set; } = "#CFCFCF";
    public string DimTextColor    { get; set; } = "#787878";
    public string BorderColor     { get; set; } = "#3A4560";
    public double BorderThickness { get; set; } = 1.0;
    public double CornerRadius    { get; set; } = 12.0;
    public int    FontSize        { get; set; } = 12;

    // 動作
    public bool AlwaysOnTop  { get; set; } = false;
    public bool DesktopMode  { get; set; } = false;
    public bool ShowEvents   { get; set; } = true;
    public bool ShowTasks    { get; set; } = true;
    public bool IsVisible    { get; set; } = false;
}

// ══════════════════════════════════════════════
//  栞ウィジェット設定
// ══════════════════════════════════════════════
public class BookmarkWidgetSettings
{
    /// <summary>
    /// タブの画面 Left 座標。NaN = 未設定（初回起動時に右端へ自動配置）
    /// ウィンドウ Left ではなく「タブの左端」を保存する点に注意。
    /// パネルが閉じているとき: Window.Left == TabLeft
    /// パネルが開いているとき: Window.Left == TabLeft - PanelWidth
    /// </summary>
    public double TabLeft   { get; set; } = double.NaN;
    public double TabTop    { get; set; } = double.NaN;
    public bool   IsVisible { get; set; } = true;
}

// ══════════════════════════════════════════════
//  カテゴリープリセット
// ══════════════════════════════════════════════
public class CategoryPreset
{
    public string Id   { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public List<CategoryPresetItem> Categories { get; set; } = new();
}

public class CategoryPresetItem
{
    public string Name        { get; set; } = "";
    public string Color       { get; set; } = "#3D7EFF";
    public string Description { get; set; } = "";
}

// ══════════════════════════════════════════════
//  ポモドーロタイマー設定
// ══════════════════════════════════════════════
public class PomodoroSettings
{
    public int WorkMinutes      { get; set; } = 25;
    public int ShortBreakMinutes{ get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int SessionsBeforeLongBreak { get; set; } = 4;
    public bool StopMediaOnBreak{ get; set; } = false;
    public bool NotifyOnComplete{ get; set; } = true;
}

// ══════════════════════════════════════════════
//  スケジュール（カレンダーイベント）
// ══════════════════════════════════════════════
public class ScheduleEvent
{
    public string    Id          { get; set; } = Guid.NewGuid().ToString("N");
    public string    Title       { get; set; } = "";
    public string    Description { get; set; } = "";
    public string    Location    { get; set; } = "";
    public DateTime  StartTime   { get; set; } = DateTime.Now;
    public DateTime  EndTime     { get; set; } = DateTime.Now.AddHours(1);
    public bool      IsAllDay    { get; set; } = false;
    public string    Color       { get; set; } = "#3D7EFF";
    public bool      IsRecurring { get; set; } = false;
    public string?   LinkedTaskId{ get; set; }   // タスクと連携の場合
    public DateTime  CreatedAt   { get; set; } = DateTime.Now;
}

// ══════════════════════════════════════════════
//  記事作成
// ══════════════════════════════════════════════
public class Article
{
    public string    Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string    Title     { get; set; } = "";
    public string    Content   { get; set; } = "";
    public string    Platform  { get; set; } = "note";  // "note" or "wordpress"
    public List<string> Tags   { get; set; } = new();
    public string    Category  { get; set; } = "その他";
    public DateTime  CreatedAt { get; set; } = DateTime.Now;
    public DateTime  UpdatedAt { get; set; } = DateTime.Now;
    public bool      IsDraft   { get; set; } = true;
    public string    Status    { get; set; } = "draft";
}

public class WordPressConfig
{
    public string SiteUrl    { get; set; } = "";
    public string Username   { get; set; } = "";
    public string AppPassword{ get; set; } = "";
}

public class ArticleAppSettings
{
    public WordPressConfig WordPress { get; set; } = new();
}

public class ThemeColors
{
    public string? TextPrimary   { get; set; }
    public string? TextSecond    { get; set; }
    public string? AccentCyan    { get; set; }
    public string? BgSecondary   { get; set; }
    public string? BgCard        { get; set; }
    public string? Border        { get; set; }
    public double? BgCardOpacity { get; set; }
    public string? FontFamily    { get; set; }
    public string? ButtonBg      { get; set; }
    public string? ButtonBorder  { get; set; }
    public string? DropdownBg { get; set; }
}

// ホーム画面セクションごとのテーマ
public class SectionTheme
{
    public string? BgColor     { get; set; }
    public string? TextColor   { get; set; }
    public string? BorderColor { get; set; }
    public double  Opacity     { get; set; } = 1.0;
}

// ══════════════════════════════════════════════
//  テーマプリセット
// ══════════════════════════════════════════════
public class ThemePreset
{
    public string Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name      { get; set; } = "";
    public bool   IsBuiltIn { get; set; } = false;
    public ThemeColors Theme { get; set; } = new();
    /// <summary>
    /// 各コンポーネントのセクションテーマ。
    /// 空の場合はプリセット適用時にすべてのセクションテーマをリセットする。
    /// </summary>
    public Dictionary<string, SectionTheme> SectionThemes { get; set; } = new();
}

// ホーム画面レイアウト（グリッドセル割当ベース）
public class HomeLayoutSlot
{
    public string ComponentId { get; set; } = "";
    public bool   Visible     { get; set; } = true;
    public int    Row         { get; set; } = 0;
    public int    Column      { get; set; } = 0;
    public int    RowSpan     { get; set; } = 1;
    public int    ColumnSpan  { get; set; } = 1;
    // 後方互換のため残置（無視）
    public bool   IsRightColumn { get; set; } = false;
    public int    Order         { get; set; } = 0;
    public double MinHeight     { get; set; } = 0;
    public double Height        { get; set; } = 0;
}

public class AppShortcut
{
    public string Id   { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🔗";
    public string Path { get; set; } = string.Empty;
}

public class ActualWorkEntry
{
    public string TaskId { get; set; } = string.Empty;
    public DateTime Date  { get; set; }
    public double Hours   { get; set; }
}

public class ProjectEntry
{
    public string ProjectName  { get; set; } = string.Empty;
    public string DataFilePath { get; set; } = string.Empty;
    public string ProjectPath  { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; } = DateTime.Now;
    public string Description  { get; set; } = string.Empty;
    public bool IsPinned       { get; set; } = false;
}

public class ProjectSettings
{
    public string ProjectName  { get; set; } = string.Empty;
    public string ProjectPath  { get; set; } = string.Empty;
    public string Description  { get; set; } = string.Empty;
    public DateTime CreatedAt  { get; set; } = DateTime.Now;
    public string Version      { get; set; } = "1.0.0";
}

public partial class Category : ObservableObject
{
    [ObservableProperty] private string _id          = string.Empty;
    [ObservableProperty] private string _name        = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _color       = "#3D7EFF";
    [ObservableProperty] private string _folderPath  = string.Empty;
    [ObservableProperty] private bool   _folderCreated = false;
    /// <summary>優先度順（1始まり）。フォルダ名プレフィックスに使用。</summary>
    public int Order { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public ObservableCollection<TaskItem> Tasks { get; set; } = new();
}

public class TaskComment
{
    public string Id         { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Author     { get; set; } = string.Empty;
    public string Text       { get; set; } = string.Empty;
    public DateTime PostedAt { get; set; } = DateTime.Now;
}

public partial class TaskItem : ObservableObject
{
    [ObservableProperty] private string _id           = string.Empty;
    [ObservableProperty] private string _categoryId   = string.Empty;
    [ObservableProperty] private string _name         = string.Empty;
    [ObservableProperty] private string _nameShort    = string.Empty;
    [ObservableProperty] private string _subCategory  = string.Empty;
    [ObservableProperty] private string _environment  = string.Empty;
    [ObservableProperty] private string _assignee     = string.Empty;
    [ObservableProperty] private string _description  = string.Empty;
    [ObservableProperty] private string _priority     = "中";
    [ObservableProperty] private string _status       = "未着手";
    [ObservableProperty] private string _notes        = string.Empty;
    [ObservableProperty] private DateTime? _plannedStartDate;
    [ObservableProperty] private DateTime? _plannedEndDate;
    [ObservableProperty] private DateTime? _actualStartDate;
    [ObservableProperty] private DateTime? _actualEndDate;
    [ObservableProperty] private string _folderPath     = string.Empty;
    [ObservableProperty] private bool   _folderCreated  = false;
    [ObservableProperty] private bool   _movedToComplete = false;
    [ObservableProperty] private bool   _delayApproved  = false;
    [ObservableProperty] private string _tags           = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public List<TaskComment> Comments { get; set; } = new();
    /// <summary>進捗反映タグ: 変更監視対象ファイルのフルパス一覧（空=全ファイル監視）</summary>
    public List<string> ProgressTagFiles { get; set; } = new();
    /// <summary>進捗達成条件リスト</summary>
    public List<ProgressCondition> ProgressConditions { get; set; } = new();

    public string FolderName => $"{Id}_{NameShort}";
    public bool   IsCompleted => Status == "完了";

    public bool IsOverdue =>
        !DelayApproved && Status != "完了" &&
        PlannedEndDate.HasValue && PlannedEndDate.Value.Date < DateTime.Today;

    public bool IsDueSoon =>
        Status != "完了" && !IsOverdue &&
        PlannedEndDate.HasValue &&
        PlannedEndDate.Value.Date >= DateTime.Today &&
        PlannedEndDate.Value.Date <= DateTime.Today.AddDays(3);

    public bool IsNotStartedOverdue(int graceDays = 1) =>
        !DelayApproved && Status == "未着手" &&
        PlannedStartDate.HasValue &&
        PlannedStartDate.Value.Date.AddDays(graceDays) < DateTime.Today;

    public bool IsNotStarted => IsNotStartedOverdue(1);

    public int? RemainingDays =>
        PlannedEndDate.HasValue
            ? (int)(PlannedEndDate.Value.Date - DateTime.Today).TotalDays
            : null;

    public int? DelayDays =>
        PlannedEndDate.HasValue && ActualEndDate.HasValue
            ? (int)(ActualEndDate.Value.Date - PlannedEndDate.Value.Date).TotalDays
            : null;
}

public class ProjectData
{
    public ProjectSettings Settings    { get; set; } = new();
    public List<Category>  Categories  { get; set; } = new();
    public List<TaskItem>  Tasks       { get; set; } = new();
    public List<ActualWorkEntry> ActualWork { get; set; } = new();
    public List<CustomTable> CustomTables  { get; set; } = new();
    public DateTime LastSaved          { get; set; } = DateTime.Now;
    public string ProjectVersion       { get; set; } = "1.0.0";
    public string Manager              { get; set; } = string.Empty;
}

// ── 表作成ツール ─────────────────────────────
public class CustomTable
{
    public string Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name      { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<TableColumn> Columns { get; set; } = new();
    public List<TableRow>    Rows    { get; set; } = new();
}

public class TableColumn
{
    public string Id    { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name  { get; set; } = "";
    // "text" | "number" | "autonumber"
    public string Type  { get; set; } = "text";
    public int    Order { get; set; }
}

public class TableRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    // columnId → value
    public Dictionary<string, string> Cells { get; set; } = new();
}

public class AlertItem
{
    public string     ProjectName   { get; set; } = string.Empty;
    public string     TaskId        { get; set; } = string.Empty;
    public string     TaskName      { get; set; } = string.Empty;
    public string     Assignee      { get; set; } = string.Empty;
    public AlertLevel Level         { get; set; }
    public string LevelLabel => Level switch
    {
        AlertLevel.Overdue    => "期限超過",
        AlertLevel.DueSoon    => "締切間近",
        AlertLevel.NotStarted => "未着手超過",
        _ => ""
    };
    public string LevelIcon => Level switch
    {
        AlertLevel.Overdue    => "🔴",
        AlertLevel.DueSoon    => "🟡",
        AlertLevel.NotStarted => "🟠",
        _ => "⚪"
    };
    public DateTime? PlannedEndDate { get; set; }
    public int? RemainingDays       { get; set; }
    public string RemainingLabel => RemainingDays switch
    {
        null => "-",
        < 0  => $"{-RemainingDays}日超過",
        0    => "今日が期限",
        _    => $"残り{RemainingDays}日"
    };
    public TaskItem? Source    { get; set; }
    public string DataFilePath { get; set; } = string.Empty;
}

public enum AlertLevel { Overdue, DueSoon, NotStarted }

public class ProjectSummary
{
    public ProjectEntry Entry   { get; set; } = new();
    public int TotalTasks       { get; set; }
    public int DoneTasks        { get; set; }
    public int WipTasks         { get; set; }
    public int OverdueTasks     { get; set; }
    public DateTime CreatedAt   { get; set; }
    public double ProgressRate  => TotalTasks > 0 ? (double)DoneTasks / TotalTasks * 100 : 0;
    public string ProgressLabel => $"{ProgressRate:F0}%";
    public bool HasAlert        => OverdueTasks > 0;
}

public class FileNode
{
    public string Name      { get; set; } = string.Empty;
    public string FullPath  { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public bool IsExpanded  { get; set; }
    public List<FileNode> Children { get; set; } = new();
    public string Icon => IsDirectory ? "📁" : GetFileIcon(Name);

    private static string GetFileIcon(string name)
    {
        var ext = System.IO.Path.GetExtension(name).ToLower();
        return ext switch
        {
            ".xlsx" or ".xls"                     => "📊",
            ".docx" or ".doc"                     => "📝",
            ".pptx" or ".ppt"                     => "📋",
            ".pdf"                                 => "📄",
            ".png" or ".jpg" or ".jpeg" or ".gif" => "🖼️",
            ".zip" or ".rar" or ".7z"             => "🗜️",
            ".cs" or ".py" or ".js" or ".ts"      => "💻",
            _ => "📄"
        };
    }
}

public class GanttRow
{
    public string    Id           { get; set; } = string.Empty;
    public string    Label        { get; set; } = string.Empty;
    public int       IndentLevel  { get; set; }
    public bool      IsCategory   { get; set; }
    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd   { get; set; }
    public DateTime? ActualStart  { get; set; }
    public DateTime? ActualEnd    { get; set; }
    public string    Status       { get; set; } = string.Empty;
    public string    Priority     { get; set; } = string.Empty;
    public string    Assignee     { get; set; } = string.Empty;
    public TaskItem? Task         { get; set; }
}

public class CalendarCell
{
    public DateTime Date              { get; set; }
    public bool IsCurrentMonth        { get; set; }
    public bool IsToday               => Date.Date == DateTime.Today;
    public List<TaskItem> PlannedTasks { get; set; } = new();
    public List<TaskItem> ActualTasks  { get; set; } = new();
}

public static class StatusValues
{
    public static readonly string[] All = { "未着手", "対応中", "レビュー中", "完了" };
}

public static class PriorityValues
{
    public static readonly string[] All = { "高", "中", "低" };
}

public static class AppVersion
{
    public const string Current     = "1.1.0";
    public const string BuildDate   = "2025-06-21";
    public const string DisplayName = "TKer v" + Current;
}

// ══════════════════════════════════════════════
//  ログ機能
// ══════════════════════════════════════════════
public enum AppLogLevel { DEBUG, INFO, WARN, ERROR, FATAL }

public class LogEntry
{
    public DateTime     Timestamp    { get; set; } = DateTime.Now;
    public AppLogLevel  Level        { get; set; } = AppLogLevel.INFO;
    public string       ScreenName   { get; set; } = "";
    public string       FunctionName { get; set; } = "";
    public string       Message      { get; set; } = "";
    public string?      StackTrace   { get; set; }

    public string LevelLabel => Level.ToString();
    public string FormattedLine =>
        $"{Timestamp:yyyy/MM/dd HH:mm:ss} [{Level,-5}] [{ScreenName}] [{FunctionName}] {Message}" +
        (StackTrace != null ? $"\n{StackTrace}" : "");
}

public class LogRotationSettings
{
    /// <summary>ローテーション有効</summary>
    public bool   Enabled         { get; set; } = true;
    /// <summary>ローテーション周期: "daily" | "weekly" | "monthly" | "size"</summary>
    public string Period          { get; set; } = "daily";
    /// <summary>サイズローテーション時の上限 (MB)</summary>
    public int    MaxFileSizeMB   { get; set; } = 10;
    /// <summary>保持するログファイル数</summary>
    public int    MaxFiles        { get; set; } = 7;
    /// <summary>ローテーション時の処理: "backup" | "delete"</summary>
    public string OnRotate        { get; set; } = "backup";
}

// ══════════════════════════════════════════════
//  TODO機能
// ══════════════════════════════════════════════
public enum TodoRepeat { None, Daily, Weekly, Weekday, Monthly, Yearly }

public class TodoItem
{
    public string    Id           { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string    Title        { get; set; } = "";
    public string?   Notes        { get; set; }
    /// <summary>null = プロジェクト未紐付け</summary>
    public string?   LinkedTaskId { get; set; }
    public bool      IsCompleted  { get; set; } = false;
    public DateTime? DueDate      { get; set; }
    public DateTime? CompletedAt  { get; set; }
    public string    Color        { get; set; } = "#3D7EFF";
    public TodoRepeat Repeat      { get; set; } = TodoRepeat.None;
    public List<string> AttachmentFiles { get; set; } = new();
    /// <summary>地図情報（住所・座標など自由テキスト）</summary>
    public string? MapInfo        { get; set; }
    public DateTime CreatedAt     { get; set; } = DateTime.Now;
    public DateTime UpdatedAt     { get; set; } = DateTime.Now;

    public string RepeatLabel => Repeat switch
    {
        TodoRepeat.Daily   => "毎日",
        TodoRepeat.Weekly  => "毎週",
        TodoRepeat.Weekday => "平日",
        TodoRepeat.Monthly => "毎月",
        TodoRepeat.Yearly  => "毎年",
        _                  => ""
    };

    public string DueDateLabel =>
        DueDate.HasValue ? DueDate.Value.ToString("M/d") : "";

    public bool IsOverdue =>
        !IsCompleted && DueDate.HasValue && DueDate.Value.Date < DateTime.Today;
}

// ══════════════════════════════════════════════
//  タスク進捗達成条件
// ══════════════════════════════════════════════
public enum ProgressConditionType
{
    /// <summary>手動確認</summary>
    Manual,
    /// <summary>指定ファイルが存在する</summary>
    FileExists,
    /// <summary>指定アプリを起動済み</summary>
    AppLaunched,
}

// ══════════════════════════════════════════════
//  コレクション
// ══════════════════════════════════════════════

/// <summary>コレクションのアイテムに付属させる情報フィールドの定義</summary>
public class CollectionField
{
    public string Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name      { get; set; } = "";
    /// <summary>文字列 | 画像 | リンク</summary>
    public string FieldType { get; set; } = "文字列";
    public int    Order     { get; set; } = 0;
}

/// <summary>コレクション（任意のアイテムをまとめるグループ）</summary>
public class Collection
{
    public string Id          { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name        { get; set; } = "";
    public string Icon        { get; set; } = "📁";
    public string Description { get; set; } = "";
    public string FolderPath  { get; set; } = "";
    /// <summary>アイテムの主データ形式: "文字列" | "ファイル"</summary>
    public string ItemFormat  { get; set; } = "文字列";
    public List<CollectionField> Fields { get; set; } = new();
    public List<CollectionItem>  Items  { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>コレクション内の1件のアイテム</summary>
public class CollectionItem
{
    public string Id      { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name    { get; set; } = "";
    /// <summary>フィールドID → 値 の動的データ</summary>
    public Dictionary<string, string> FieldValues { get; set; } = new();
    public DateTime AddedAt { get; set; } = DateTime.Now;
}

public class ProgressCondition
{
    public string                Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string                Label     { get; set; } = "";
    public ProgressConditionType Type      { get; set; } = ProgressConditionType.Manual;
    /// <summary>FileExists / AppLaunched 用のパス</summary>
    public string?               Path      { get; set; }
    public bool                  IsAchieved{ get; set; } = false;
    public DateTime?             AchievedAt{ get; set; }
}

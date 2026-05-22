using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TKer.Models;

/// <summary>アプリケーション全体の設定を保持するクラス。</summary>
public class AppSettings
{
    /// <summary>アプリケーションのバージョン文字列。</summary>
    public string AppVersion { get; set; } = "1.0.0";
    /// <summary>最近開いたプロジェクトの一覧。</summary>
    public List<ProjectEntry> RecentProjects { get; set; } = new();
    /// <summary>最後に開いたプロジェクトファイルのパス。</summary>
    public string? LastOpenedProjectPath { get; set; }
    /// <summary>最後にアプリを起動した日時。</summary>
    public DateTime LastLaunched { get; set; } = DateTime.Now;
    /// <summary>未着手超過と判定するまでの猶予日数。</summary>
    public int AlertNotStartedGraceDays { get; set; } = 1;
    /// <summary>スケジュール画面で完了タスクを表示するかどうか。</summary>
    public bool ShowCompletedInSchedule { get; set; } = false;
    /// <summary>背景画像のファイルパス。</summary>
    public string? BackgroundImagePath { get; set; }
    /// <summary>アプリの動作モード（"Multi" など）。</summary>
    public string AppMode { get; set; } = "Multi";
    /// <summary>登録済みアプリショートカットの一覧。</summary>
    public List<AppShortcut> Shortcuts { get; set; } = new();
    /// <summary>メニューの表示順序。</summary>
    public List<string> MenuOrder { get; set; } = new() { "ホーム", "ライブラリ", "タスク管理", "ツール", "カスタマイズ", "ヘルプ" };
    /// <summary>ホーム画面のレイアウトスロット定義。</summary>
    public Dictionary<string, HomeLayoutSlot> HomeLayout { get; set; } = new();
    /// <summary>ホーム画面の列幅。要素の意味: &gt;0=px, 0=Auto, &lt;0=Star（-1なら1*, -2なら2*）</summary>
    public List<double> HomeColumnWidths { get; set; } = new();
    /// <summary>ホーム画面の行高さ。同上</summary>
    public List<double> HomeRowHeights   { get; set; } = new();
    /// <summary>テーマカラー設定。</summary>
    public ThemeColors Theme { get; set; } = new();
    /// <summary>再起動確認ダイアログをスキップするかどうか。</summary>
    public bool SkipRestartConfirm { get; set; } = false;
    /// <summary>セクションごとのテーマ設定。</summary>
    public Dictionary<string, SectionTheme> SectionThemes { get; set; } = new();
    /// <summary>タスク行の不透明度。</summary>
    public double TaskRowOpacity        { get; set; } = 1.0;
    /// <summary>ガントチャート行の背景色。</summary>
    public string GanttRowColor         { get; set; } = "#EBEBEB";
    /// <summary>ガントチャート行の不透明度。</summary>
    public double GanttRowOpacity       { get; set; } = 1.0;
    /// <summary>行ボーダーの色。</summary>
    public string RowBorderColor        { get; set; } = "#606060";
    /// <summary>プロジェクト一覧の背景色。</summary>
    public string ProjectListBgColor    { get; set; } = "#2F2F2F";
    /// <summary>プロジェクト一覧の背景不透明度。</summary>
    public double ProjectListBgOpacity  { get; set; } = 1.0;
    /// <summary>カテゴリー一覧の背景色。</summary>
    public string CategoryListBgColor   { get; set; } = "#2F2F2F";
    /// <summary>カテゴリー一覧の背景不透明度。</summary>
    public double CategoryListBgOpacity { get; set; } = 1.0;
    /// <summary>アプリ設定画面の背景色。</summary>
    public string AppSettingsBgColor    { get; set; } = "#2F2F2F";
    /// <summary>アプリ設定画面の背景不透明度。</summary>
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
/// <summary>カレンダーウィジェットの表示・動作設定を保持するクラス。</summary>
public class CalendarWidgetSettings
{
    // 位置・サイズ
    /// <summary>ウィジェットの画面左端座標。</summary>
    public double Left   { get; set; } = 40;
    /// <summary>ウィジェットの画面上端座標。</summary>
    public double Top    { get; set; } = 80;
    /// <summary>ウィジェットの幅。</summary>
    public double Width  { get; set; } = 280;
    /// <summary>ウィジェットの高さ。</summary>
    public double Height { get; set; } = 300;

    // 外観
    /// <summary>ウィジェットの不透明度。</summary>
    public double Opacity         { get; set; } = 0.92;
    /// <summary>背景色（ARGB 16進数）。</summary>
    public string BackgroundColor { get; set; } = "#E61A1F2E";
    /// <summary>アクセントカラー。</summary>
    public string AccentColor     { get; set; } = "#3D7EFF";
    /// <summary>テキストの基本色。</summary>
    public string TextColor       { get; set; } = "#CFCFCF";
    /// <summary>サブテキストの色。</summary>
    public string DimTextColor    { get; set; } = "#787878";
    /// <summary>ボーダーの色。</summary>
    public string BorderColor     { get; set; } = "#3A4560";
    /// <summary>ボーダーの太さ。</summary>
    public double BorderThickness { get; set; } = 1.0;
    /// <summary>角丸の半径。</summary>
    public double CornerRadius    { get; set; } = 12.0;
    /// <summary>フォントサイズ。</summary>
    public int    FontSize        { get; set; } = 12;

    // 動作
    /// <summary>常に最前面に表示するかどうか。</summary>
    public bool AlwaysOnTop  { get; set; } = false;
    /// <summary>デスクトップモード（タスクバー等に影響しない位置）で表示するかどうか。</summary>
    public bool DesktopMode  { get; set; } = false;
    /// <summary>イベントを表示するかどうか。</summary>
    public bool ShowEvents   { get; set; } = true;
    /// <summary>タスクを表示するかどうか。</summary>
    public bool ShowTasks    { get; set; } = true;
    /// <summary>ウィジェットを表示するかどうか。</summary>
    public bool IsVisible    { get; set; } = false;
}

// ══════════════════════════════════════════════
//  栞ウィジェット設定
// ══════════════════════════════════════════════
/// <summary>栞ウィジェットの表示位置・可視状態を保持するクラス。</summary>
public class BookmarkWidgetSettings
{
    /// <summary>
    /// タブの画面 Left 座標。NaN = 未設定（初回起動時に右端へ自動配置）
    /// ウィンドウ Left ではなく「タブの左端」を保存する点に注意。
    /// パネルが閉じているとき: Window.Left == TabLeft
    /// パネルが開いているとき: Window.Left == TabLeft - PanelWidth
    /// </summary>
    public double TabLeft   { get; set; } = double.NaN;
    /// <summary>タブの画面 Top 座標。NaN = 未設定。</summary>
    public double TabTop    { get; set; } = double.NaN;
    /// <summary>ウィジェットを表示するかどうか。</summary>
    public bool   IsVisible { get; set; } = true;
    /// <summary>位置を固定するかどうか。true の場合は端への自動スナップを行わない。</summary>
    public bool   IsPinned  { get; set; } = false;
}

// ══════════════════════════════════════════════
//  カテゴリープリセット
// ══════════════════════════════════════════════
/// <summary>ユーザーが定義したカテゴリープリセットを表すクラス。</summary>
public class CategoryPreset
{
    /// <summary>プリセットの一意識別子。</summary>
    public string Id   { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>プリセット名。</summary>
    public string Name { get; set; } = "";
    /// <summary>プリセットに含まれるカテゴリーアイテムの一覧。</summary>
    public List<CategoryPresetItem> Categories { get; set; } = new();
}

/// <summary>カテゴリープリセット内の個別カテゴリー定義を表すクラス。</summary>
public class CategoryPresetItem
{
    /// <summary>カテゴリー名。</summary>
    public string Name        { get; set; } = "";
    /// <summary>カテゴリーの表示色。</summary>
    public string Color       { get; set; } = "#3D7EFF";
    /// <summary>カテゴリーの説明文。</summary>
    public string Description { get; set; } = "";
}

// ══════════════════════════════════════════════
//  ポモドーロタイマー設定
// ══════════════════════════════════════════════
/// <summary>ポモドーロタイマーの各種時間設定を保持するクラス。</summary>
public class PomodoroSettings
{
    /// <summary>作業セッションの時間（分）。</summary>
    public int WorkMinutes      { get; set; } = 25;
    /// <summary>短い休憩の時間（分）。</summary>
    public int ShortBreakMinutes{ get; set; } = 5;
    /// <summary>長い休憩の時間（分）。</summary>
    public int LongBreakMinutes { get; set; } = 15;
    /// <summary>長い休憩に入るまでのセッション数。</summary>
    public int SessionsBeforeLongBreak { get; set; } = 4;
    /// <summary>休憩開始時にメディア再生を停止するかどうか。</summary>
    public bool StopMediaOnBreak{ get; set; } = false;
    /// <summary>セッション完了時に通知するかどうか。</summary>
    public bool NotifyOnComplete{ get; set; } = true;
}

// ══════════════════════════════════════════════
//  スケジュール（カレンダーイベント）
// ══════════════════════════════════════════════
/// <summary>カレンダーに表示するスケジュールイベントを表すクラス。</summary>
public class ScheduleEvent
{
    /// <summary>イベントの一意識別子。</summary>
    public string    Id          { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>イベントのタイトル。</summary>
    public string    Title       { get; set; } = "";
    /// <summary>イベントの詳細説明。</summary>
    public string    Description { get; set; } = "";
    /// <summary>イベントの開催場所。</summary>
    public string    Location    { get; set; } = "";
    /// <summary>開始日時。</summary>
    public DateTime  StartTime   { get; set; } = DateTime.Now;
    /// <summary>終了日時。</summary>
    public DateTime  EndTime     { get; set; } = DateTime.Now.AddHours(1);
    /// <summary>終日イベントかどうか。</summary>
    public bool      IsAllDay    { get; set; } = false;
    /// <summary>イベントの表示色。</summary>
    public string    Color       { get; set; } = "#3D7EFF";
    /// <summary>繰り返しイベントかどうか。</summary>
    public bool      IsRecurring { get; set; } = false;
    /// <summary>連携しているタスクのID。タスクと連携の場合。</summary>
    public string?   LinkedTaskId{ get; set; }
    /// <summary>イベントの作成日時。</summary>
    public DateTime  CreatedAt   { get; set; } = DateTime.Now;
}

// ══════════════════════════════════════════════
//  記事作成
// ══════════════════════════════════════════════
/// <summary>ブログ記事やノート投稿のデータを保持するクラス。</summary>
public class Article
{
    /// <summary>記事の一意識別子。</summary>
    public string    Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>記事のタイトル。</summary>
    public string    Title     { get; set; } = "";
    /// <summary>記事の本文。</summary>
    public string    Content   { get; set; } = "";
    /// <summary>投稿先プラットフォーム（"note" または "wordpress"）。</summary>
    public string    Platform  { get; set; } = "note";
    /// <summary>記事に付与するタグの一覧。</summary>
    public List<string> Tags   { get; set; } = new();
    /// <summary>記事のカテゴリー。</summary>
    public string    Category  { get; set; } = "その他";
    /// <summary>記事の作成日時。</summary>
    public DateTime  CreatedAt { get; set; } = DateTime.Now;
    /// <summary>記事の最終更新日時。</summary>
    public DateTime  UpdatedAt { get; set; } = DateTime.Now;
    /// <summary>下書き状態かどうか。</summary>
    public bool      IsDraft   { get; set; } = true;
    /// <summary>記事の公開ステータス。</summary>
    public string    Status    { get; set; } = "draft";
}

/// <summary>WordPress への接続設定を保持するクラス。</summary>
public class WordPressConfig
{
    /// <summary>WordPress サイトの URL。</summary>
    public string SiteUrl    { get; set; } = "";
    /// <summary>ログインユーザー名。</summary>
    public string Username   { get; set; } = "";
    /// <summary>アプリケーションパスワード。</summary>
    public string AppPassword{ get; set; } = "";
}

/// <summary>記事作成機能のアプリ設定を保持するクラス。</summary>
public class ArticleAppSettings
{
    /// <summary>WordPress 接続設定。</summary>
    public WordPressConfig WordPress { get; set; } = new();
}

/// <summary>UI テーマのカラー設定を保持するクラス。</summary>
public class ThemeColors
{
    /// <summary>主要テキストの色。</summary>
    public string? TextPrimary   { get; set; }
    /// <summary>補助テキストの色。</summary>
    public string? TextSecond    { get; set; }
    /// <summary>シアン系アクセントカラー。</summary>
    public string? AccentCyan    { get; set; }
    /// <summary>セカンダリ背景色。</summary>
    public string? BgSecondary   { get; set; }
    /// <summary>カードの背景色。</summary>
    public string? BgCard        { get; set; }
    /// <summary>ボーダーの色。</summary>
    public string? Border        { get; set; }
    /// <summary>カード背景の不透明度。</summary>
    public double? BgCardOpacity { get; set; }
    /// <summary>使用フォントファミリー。</summary>
    public string? FontFamily    { get; set; }
    /// <summary>ボタンの背景色。</summary>
    public string? ButtonBg      { get; set; }
    /// <summary>ボタンのボーダー色。</summary>
    public string? ButtonBorder  { get; set; }
    /// <summary>ドロップダウンの背景色。</summary>
    public string? DropdownBg { get; set; }
}

/// <summary>ホーム画面の各セクションに適用するテーマ設定を保持するクラス。</summary>
public class SectionTheme
{
    /// <summary>セクションの背景色。</summary>
    public string? BgColor     { get; set; }
    /// <summary>セクションのテキスト色。</summary>
    public string? TextColor   { get; set; }
    /// <summary>セクションのボーダー色。</summary>
    public string? BorderColor { get; set; }
    /// <summary>セクションの不透明度。</summary>
    public double  Opacity     { get; set; } = 1.0;
}

// ══════════════════════════════════════════════
//  テーマプリセット
// ══════════════════════════════════════════════
/// <summary>保存・適用可能なテーマプリセットを表すクラス。</summary>
public class ThemePreset
{
    /// <summary>プリセットの一意識別子。</summary>
    public string Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>プリセット名。</summary>
    public string Name      { get; set; } = "";
    /// <summary>ビルトインプリセットかどうか。</summary>
    public bool   IsBuiltIn { get; set; } = false;
    /// <summary>テーマカラー設定。</summary>
    public ThemeColors Theme { get; set; } = new();
    /// <summary>
    /// 各コンポーネントのセクションテーマ。
    /// 空の場合はプリセット適用時にすべてのセクションテーマをリセットする。
    /// </summary>
    public Dictionary<string, SectionTheme> SectionThemes { get; set; } = new();
}

/// <summary>ホーム画面グリッドの1セルへのコンポーネント割当情報を保持するクラス。</summary>
public class HomeLayoutSlot
{
    /// <summary>配置するコンポーネントのID。</summary>
    public string ComponentId { get; set; } = "";
    /// <summary>スロットを表示するかどうか。</summary>
    public bool   Visible     { get; set; } = true;
    /// <summary>グリッド上の行インデックス。</summary>
    public int    Row         { get; set; } = 0;
    /// <summary>グリッド上の列インデックス。</summary>
    public int    Column      { get; set; } = 0;
    /// <summary>占有する行数。</summary>
    public int    RowSpan     { get; set; } = 1;
    /// <summary>占有する列数。</summary>
    public int    ColumnSpan  { get; set; } = 1;
    // 後方互換のため残置（無視）
    /// <summary>右列配置フラグ（後方互換のため残置。現在は無視）。</summary>
    public bool   IsRightColumn { get; set; } = false;
    /// <summary>表示順序。</summary>
    public int    Order         { get; set; } = 0;
    /// <summary>スロットの最小高さ。</summary>
    public double MinHeight     { get; set; } = 0;
    /// <summary>スロットの高さ。</summary>
    public double Height        { get; set; } = 0;
}

/// <summary>アプリのショートカット登録情報を保持するクラス。</summary>
public class AppShortcut
{
    /// <summary>ショートカットの一意識別子。</summary>
    public string Id   { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>ショートカットの表示名。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>ショートカットのアイコン（絵文字）。</summary>
    public string Icon { get; set; } = "🔗";
    /// <summary>起動するアプリやファイルのパス。</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>タスクの実績作業時間エントリーを表すクラス。</summary>
public class ActualWorkEntry
{
    /// <summary>対象タスクのID。</summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>作業日。</summary>
    public DateTime Date  { get; set; }
    /// <summary>作業時間（時間単位）。</summary>
    public double Hours   { get; set; }
}

/// <summary>最近開いたプロジェクトの参照情報を保持するクラス。</summary>
public class ProjectEntry
{
    /// <summary>プロジェクト名。</summary>
    public string ProjectName  { get; set; } = string.Empty;
    /// <summary>プロジェクトのデータファイルパス。</summary>
    public string DataFilePath { get; set; } = string.Empty;
    /// <summary>プロジェクトのルートフォルダパス。</summary>
    public string ProjectPath  { get; set; } = string.Empty;
    /// <summary>最後に開いた日時。</summary>
    public DateTime LastOpened { get; set; } = DateTime.Now;
    /// <summary>プロジェクトの説明文。</summary>
    public string Description  { get; set; } = string.Empty;
    /// <summary>ピン留め状態かどうか。</summary>
    public bool IsPinned       { get; set; } = false;
}

/// <summary>プロジェクト固有の設定情報を保持するクラス。</summary>
public class ProjectSettings
{
    /// <summary>プロジェクト名。</summary>
    public string ProjectName  { get; set; } = string.Empty;
    /// <summary>プロジェクトのルートフォルダパス。</summary>
    public string ProjectPath  { get; set; } = string.Empty;
    /// <summary>プロジェクトの説明文。</summary>
    public string Description  { get; set; } = string.Empty;
    /// <summary>プロジェクト作成日時。</summary>
    public DateTime CreatedAt  { get; set; } = DateTime.Now;
    /// <summary>プロジェクトデータのバージョン文字列。</summary>
    public string Version      { get; set; } = "1.0.0";
}

/// <summary>タスクを分類するカテゴリーを表す ObservableObject クラス。</summary>
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
    /// <summary>カテゴリーの作成日時。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>このカテゴリーに属するタスクのコレクション。</summary>
    public ObservableCollection<TaskItem> Tasks { get; set; } = new();
}

/// <summary>タスクに付与するコメントを表すクラス。</summary>
public class TaskComment
{
    /// <summary>コメントの一意識別子。</summary>
    public string Id         { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>コメントの投稿者名。</summary>
    public string Author     { get; set; } = string.Empty;
    /// <summary>コメントの本文。</summary>
    public string Text       { get; set; } = string.Empty;
    /// <summary>投稿日時。</summary>
    public DateTime PostedAt { get; set; } = DateTime.Now;
}

/// <summary>個別のタスクアイテムを表す ObservableObject クラス。</summary>
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

    /// <summary>タスクの作成日時。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>タスクの最終更新日時。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    /// <summary>タスクに付与されたコメントの一覧。</summary>
    public List<TaskComment> Comments { get; set; } = new();
    /// <summary>進捗反映タグ: 変更監視対象ファイルのフルパス一覧（空=全ファイル監視）</summary>
    public List<string> ProgressTagFiles { get; set; } = new();
    /// <summary>進捗達成条件リスト</summary>
    public List<ProgressCondition> ProgressConditions { get; set; } = new();

    /// <summary>フォルダ名（ID と短縮名を結合した文字列）。</summary>
    public string FolderName => $"{Id}_{NameShort}";
    /// <summary>タスクが完了状態かどうか。</summary>
    public bool   IsCompleted => Status == "完了";

    /// <summary>期限超過かどうか（遅延承認済みまたは完了の場合は false）。</summary>
    public bool IsOverdue =>
        !DelayApproved && Status != "完了" &&
        PlannedEndDate.HasValue && PlannedEndDate.Value.Date < DateTime.Today;

    /// <summary>期限が3日以内に迫っているかどうか。</summary>
    public bool IsDueSoon =>
        Status != "完了" && !IsOverdue &&
        PlannedEndDate.HasValue &&
        PlannedEndDate.Value.Date >= DateTime.Today &&
        PlannedEndDate.Value.Date <= DateTime.Today.AddDays(3);

    /// <summary>猶予日数を超えても未着手かどうかを判定する。</summary>
    public bool IsNotStartedOverdue(int graceDays = 1) =>
        !DelayApproved && Status == "未着手" &&
        PlannedStartDate.HasValue &&
        PlannedStartDate.Value.Date.AddDays(graceDays) < DateTime.Today;

    /// <summary>1日の猶予を超えて未着手かどうか。</summary>
    public bool IsNotStarted => IsNotStartedOverdue(1);

    /// <summary>計画終了日までの残り日数。終了日未設定の場合は null。</summary>
    public int? RemainingDays =>
        PlannedEndDate.HasValue
            ? (int)(PlannedEndDate.Value.Date - DateTime.Today).TotalDays
            : null;

    /// <summary>実績終了日と計画終了日の差分（遅延日数）。どちらか未設定の場合は null。</summary>
    public int? DelayDays =>
        PlannedEndDate.HasValue && ActualEndDate.HasValue
            ? (int)(ActualEndDate.Value.Date - PlannedEndDate.Value.Date).TotalDays
            : null;
}

/// <summary>プロジェクト全体のデータ（設定・カテゴリー・タスク等）を保持するクラス。</summary>
public class ProjectData
{
    /// <summary>プロジェクト設定。</summary>
    public ProjectSettings Settings    { get; set; } = new();
    /// <summary>カテゴリーの一覧。</summary>
    public List<Category>  Categories  { get; set; } = new();
    /// <summary>タスクの一覧。</summary>
    public List<TaskItem>  Tasks       { get; set; } = new();
    /// <summary>実績作業時間エントリーの一覧。</summary>
    public List<ActualWorkEntry> ActualWork { get; set; } = new();
    /// <summary>カスタムテーブルの一覧。</summary>
    public List<CustomTable> CustomTables  { get; set; } = new();
    /// <summary>最終保存日時。</summary>
    public DateTime LastSaved          { get; set; } = DateTime.Now;
    /// <summary>プロジェクトデータのバージョン文字列。</summary>
    public string ProjectVersion       { get; set; } = "1.0.0";
    /// <summary>プロジェクト管理者名。</summary>
    public string Manager              { get; set; } = string.Empty;
}

// ── 表作成ツール ─────────────────────────────
/// <summary>ユーザーが作成したカスタムテーブルを表すクラス。</summary>
public class CustomTable
{
    /// <summary>テーブルの一意識別子。</summary>
    public string Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>テーブル名。</summary>
    public string Name      { get; set; } = "";
    /// <summary>テーブルの作成日時。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>テーブルの列定義一覧。</summary>
    public List<TableColumn> Columns { get; set; } = new();
    /// <summary>テーブルの行データ一覧。</summary>
    public List<TableRow>    Rows    { get; set; } = new();
}

/// <summary>カスタムテーブルの列定義を表すクラス。</summary>
public class TableColumn
{
    /// <summary>列の一意識別子。</summary>
    public string Id    { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>列名。</summary>
    public string Name  { get; set; } = "";
    // "text" | "number" | "autonumber"
    /// <summary>列のデータ型（"text" | "number" | "autonumber"）。</summary>
    public string Type  { get; set; } = "text";
    /// <summary>列の表示順序。</summary>
    public int    Order { get; set; }
}

/// <summary>カスタムテーブルの1行分のデータを表すクラス。</summary>
public class TableRow
{
    /// <summary>行の一意識別子。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    // columnId → value
    /// <summary>列ID をキー、セル値を値とする辞書。</summary>
    public Dictionary<string, string> Cells { get; set; } = new();
}

/// <summary>タスクのアラート情報を表すクラス。</summary>
public class AlertItem
{
    /// <summary>アラートが属するプロジェクト名。</summary>
    public string     ProjectName   { get; set; } = string.Empty;
    /// <summary>対象タスクのID。</summary>
    public string     TaskId        { get; set; } = string.Empty;
    /// <summary>対象タスクの名前。</summary>
    public string     TaskName      { get; set; } = string.Empty;
    /// <summary>タスクの担当者名。</summary>
    public string     Assignee      { get; set; } = string.Empty;
    /// <summary>アラートのレベル。</summary>
    public AlertLevel Level         { get; set; }
    /// <summary>アラートレベルの日本語ラベル。</summary>
    public string LevelLabel => Level switch
    {
        AlertLevel.Overdue    => "期限超過",
        AlertLevel.DueSoon    => "締切間近",
        AlertLevel.NotStarted => "未着手超過",
        _ => ""
    };
    /// <summary>アラートレベルを示す絵文字アイコン。</summary>
    public string LevelIcon => Level switch
    {
        AlertLevel.Overdue    => "🔴",
        AlertLevel.DueSoon    => "🟡",
        AlertLevel.NotStarted => "🟠",
        _ => "⚪"
    };
    /// <summary>タスクの計画終了日。</summary>
    public DateTime? PlannedEndDate { get; set; }
    /// <summary>期限までの残り日数（負の値は超過を示す）。</summary>
    public int? RemainingDays       { get; set; }
    /// <summary>残り日数を人間が読みやすい文字列で表したラベル。</summary>
    public string RemainingLabel => RemainingDays switch
    {
        null => "-",
        < 0  => $"{-RemainingDays}日超過",
        0    => "今日が期限",
        _    => $"残り{RemainingDays}日"
    };
    /// <summary>アラートの元となるタスクアイテム。</summary>
    public TaskItem? Source    { get; set; }
    /// <summary>プロジェクトデータファイルのパス。</summary>
    public string DataFilePath { get; set; } = string.Empty;
}

/// <summary>タスクアラートのレベルを表す列挙型。</summary>
public enum AlertLevel { Overdue, DueSoon, NotStarted }

/// <summary>プロジェクトのサマリー統計情報を保持するクラス。</summary>
public class ProjectSummary
{
    /// <summary>プロジェクトのエントリー情報。</summary>
    public ProjectEntry Entry   { get; set; } = new();
    /// <summary>タスクの総数。</summary>
    public int TotalTasks       { get; set; }
    /// <summary>完了タスク数。</summary>
    public int DoneTasks        { get; set; }
    /// <summary>対応中タスク数。</summary>
    public int WipTasks         { get; set; }
    /// <summary>期限超過タスク数。</summary>
    public int OverdueTasks     { get; set; }
    /// <summary>プロジェクトの作成日時。</summary>
    public DateTime CreatedAt   { get; set; }
    /// <summary>完了率（0〜100 のパーセンテージ）。</summary>
    public double ProgressRate  => TotalTasks > 0 ? (double)DoneTasks / TotalTasks * 100 : 0;
    /// <summary>完了率を整数パーセントで表したラベル。</summary>
    public string ProgressLabel => $"{ProgressRate:F0}%";
    /// <summary>期限超過タスクが存在するかどうか。</summary>
    public bool HasAlert        => OverdueTasks > 0;
}

/// <summary>ファイルツリーの1ノードを表すクラス。</summary>
public class FileNode
{
    /// <summary>ファイルまたはフォルダの名前。</summary>
    public string Name      { get; set; } = string.Empty;
    /// <summary>フルパス。</summary>
    public string FullPath  { get; set; } = string.Empty;
    /// <summary>ディレクトリかどうか。</summary>
    public bool IsDirectory { get; set; }
    /// <summary>ツリー上で展開されているかどうか。</summary>
    public bool IsExpanded  { get; set; }
    /// <summary>子ノードの一覧。</summary>
    public List<FileNode> Children { get; set; } = new();
    /// <summary>ファイル種別に対応する絵文字アイコン。</summary>
    public string Icon => IsDirectory ? "📁" : GetFileIcon(Name);

    /// <summary>ファイル拡張子からアイコン絵文字を返す。</summary>
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

/// <summary>ガントチャートの1行分の表示データを保持するクラス。</summary>
public class GanttRow
{
    /// <summary>行の一意識別子。</summary>
    public string    Id           { get; set; } = string.Empty;
    /// <summary>行に表示するラベル。</summary>
    public string    Label        { get; set; } = string.Empty;
    /// <summary>インデントの深さ（0 始まり）。</summary>
    public int       IndentLevel  { get; set; }
    /// <summary>カテゴリー行かどうか。</summary>
    public bool      IsCategory   { get; set; }
    /// <summary>計画開始日。</summary>
    public DateTime? PlannedStart { get; set; }
    /// <summary>計画終了日。</summary>
    public DateTime? PlannedEnd   { get; set; }
    /// <summary>実績開始日。</summary>
    public DateTime? ActualStart  { get; set; }
    /// <summary>実績終了日。</summary>
    public DateTime? ActualEnd    { get; set; }
    /// <summary>タスクのステータス。</summary>
    public string    Status       { get; set; } = string.Empty;
    /// <summary>タスクの優先度。</summary>
    public string    Priority     { get; set; } = string.Empty;
    /// <summary>タスクの担当者名。</summary>
    public string    Assignee     { get; set; } = string.Empty;
    /// <summary>元となるタスクアイテム。</summary>
    public TaskItem? Task         { get; set; }
}

/// <summary>カレンダー表示用の1日分のセルデータを表すクラス。</summary>
public class CalendarCell
{
    /// <summary>このセルが表す日付。</summary>
    public DateTime Date              { get; set; }
    /// <summary>表示中の月に属する日かどうか。</summary>
    public bool IsCurrentMonth        { get; set; }
    /// <summary>今日の日付かどうか。</summary>
    public bool IsToday               => Date.Date == DateTime.Today;
    /// <summary>この日に計画されているタスクの一覧。</summary>
    public List<TaskItem> PlannedTasks { get; set; } = new();
    /// <summary>この日に実績のあるタスクの一覧。</summary>
    public List<TaskItem> ActualTasks  { get; set; } = new();
}

/// <summary>タスクステータスの選択肢を定義する静的クラス。</summary>
public static class StatusValues
{
    /// <summary>有効なステータス値の一覧。</summary>
    public static readonly string[] ALL = { "未着手", "対応中", "レビュー中", "完了" };
}

/// <summary>タスク優先度の選択肢を定義する静的クラス。</summary>
public static class PriorityValues
{
    /// <summary>有効な優先度値の一覧。</summary>
    public static readonly string[] ALL = { "高", "中", "低" };
}

/// <summary>アプリケーションのバージョン情報を定義する静的クラス。</summary>
public static class AppVersion
{
    /// <summary>現在のバージョン番号。</summary>
    public const string CURRENT      = "1.1.0";
    /// <summary>ビルド日付。</summary>
    public const string BUILD_DATE   = "2025-06-21";
    /// <summary>UI に表示するバージョン文字列。</summary>
    public const string DISPLAY_NAME = "TKer v" + CURRENT;
}

// ══════════════════════════════════════════════
//  ログ機能
// ══════════════════════════════════════════════
/// <summary>アプリログのレベルを表す列挙型。</summary>
public enum AppLogLevel { DEBUG, INFO, WARN, ERROR, FATAL }

/// <summary>アプリログの1エントリーを表すクラス。</summary>
public class LogEntry
{
    /// <summary>ログの記録日時。</summary>
    public DateTime     Timestamp    { get; set; } = DateTime.Now;
    /// <summary>ログレベル。</summary>
    public AppLogLevel  Level        { get; set; } = AppLogLevel.INFO;
    /// <summary>ログを出力した画面名。</summary>
    public string       ScreenName   { get; set; } = "";
    /// <summary>ログを出力した関数名。</summary>
    public string       FunctionName { get; set; } = "";
    /// <summary>ログメッセージ本文。</summary>
    public string       Message      { get; set; } = "";
    /// <summary>例外発生時のスタックトレース。</summary>
    public string?      StackTrace   { get; set; }

    /// <summary>ログレベルの文字列表現。</summary>
    public string LevelLabel => Level.ToString();
    /// <summary>ログエントリーを1行形式にフォーマットした文字列。</summary>
    public string FormattedLine =>
        $"{Timestamp:yyyy/MM/dd HH:mm:ss} [{Level,-5}] [{ScreenName}] [{FunctionName}] {Message}" +
        (StackTrace != null ? $"\n{StackTrace}" : "");
}

/// <summary>ログファイルのローテーション設定を保持するクラス。</summary>
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
/// <summary>TODO アイテムの繰り返し周期を表す列挙型。</summary>
public enum TodoRepeat { None, Daily, Weekly, Weekday, Monthly, Yearly }

/// <summary>TODO リストの個別アイテムを表すクラス。</summary>
public class TodoItem
{
    /// <summary>TODO アイテムの一意識別子。</summary>
    public string    Id           { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>TODO のタイトル。</summary>
    public string    Title        { get; set; } = "";
    /// <summary>TODO の補足メモ。</summary>
    public string?   Notes        { get; set; }
    /// <summary>null = プロジェクト未紐付け</summary>
    public string?   LinkedTaskId { get; set; }
    /// <summary>完了状態かどうか。</summary>
    public bool      IsCompleted  { get; set; } = false;
    /// <summary>期限日。</summary>
    public DateTime? DueDate      { get; set; }
    /// <summary>完了した日時。</summary>
    public DateTime? CompletedAt  { get; set; }
    /// <summary>表示色。</summary>
    public string    Color        { get; set; } = "#3D7EFF";
    /// <summary>繰り返し設定。</summary>
    public TodoRepeat Repeat      { get; set; } = TodoRepeat.None;
    /// <summary>添付ファイルのパス一覧。</summary>
    public List<string> AttachmentFiles { get; set; } = new();
    /// <summary>地図情報（住所・座標など自由テキスト）</summary>
    public string? MapInfo        { get; set; }
    /// <summary>TODO の作成日時。</summary>
    public DateTime CreatedAt     { get; set; } = DateTime.Now;
    /// <summary>TODO の最終更新日時。</summary>
    public DateTime UpdatedAt     { get; set; } = DateTime.Now;

    /// <summary>繰り返し設定を日本語で表したラベル。</summary>
    public string RepeatLabel => Repeat switch
    {
        TodoRepeat.Daily   => "毎日",
        TodoRepeat.Weekly  => "毎週",
        TodoRepeat.Weekday => "平日",
        TodoRepeat.Monthly => "毎月",
        TodoRepeat.Yearly  => "毎年",
        _                  => ""
    };

    /// <summary>期限日を "M/d" 形式で表したラベル。未設定の場合は空文字。</summary>
    public string DueDateLabel =>
        DueDate.HasValue ? DueDate.Value.ToString("M/d") : "";

    /// <summary>未完了かつ期限を過ぎているかどうか。</summary>
    public bool IsOverdue =>
        !IsCompleted && DueDate.HasValue && DueDate.Value.Date < DateTime.Today;
}

// ══════════════════════════════════════════════
//  タスク進捗達成条件
// ══════════════════════════════════════════════
/// <summary>タスク進捗達成条件の種別を表す列挙型。</summary>
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
    /// <summary>フィールドの一意識別子。</summary>
    public string Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>フィールド名。</summary>
    public string Name      { get; set; } = "";
    /// <summary>文字列 | 画像 | リンク</summary>
    public string FieldType { get; set; } = "文字列";
    /// <summary>フィールドの表示順序。</summary>
    public int    Order     { get; set; } = 0;
}

/// <summary>コレクション（任意のアイテムをまとめるグループ）</summary>
public class Collection
{
    /// <summary>コレクションの一意識別子。</summary>
    public string Id          { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>コレクション名。</summary>
    public string Name        { get; set; } = "";
    /// <summary>コレクションのアイコン（絵文字）。</summary>
    public string Icon        { get; set; } = "📁";
    /// <summary>コレクションの説明文。</summary>
    public string Description { get; set; } = "";
    /// <summary>関連するフォルダのパス。</summary>
    public string FolderPath  { get; set; } = "";
    /// <summary>アイテムの主データ形式: "文字列" | "ファイル"</summary>
    public string ItemFormat  { get; set; } = "文字列";
    /// <summary>コレクションのフィールド定義一覧。</summary>
    public List<CollectionField> Fields { get; set; } = new();
    /// <summary>コレクションに含まれるアイテムの一覧。</summary>
    public List<CollectionItem>  Items  { get; set; } = new();
    /// <summary>コレクションの作成日時。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>コレクションの最終更新日時。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>コレクション内の1件のアイテム</summary>
public class CollectionItem
{
    /// <summary>アイテムの一意識別子。</summary>
    public string Id      { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>アイテム名。</summary>
    public string Name    { get; set; } = "";
    /// <summary>フィールドID → 値 の動的データ</summary>
    public Dictionary<string, string> FieldValues { get; set; } = new();
    /// <summary>アイテムの追加日時。</summary>
    public DateTime AddedAt { get; set; } = DateTime.Now;
}

/// <summary>タスクの進捗達成条件を表すクラス。</summary>
public class ProgressCondition
{
    /// <summary>条件の一意識別子。</summary>
    public string                Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>条件の表示ラベル。</summary>
    public string                Label     { get; set; } = "";
    /// <summary>条件の種別。</summary>
    public ProgressConditionType Type      { get; set; } = ProgressConditionType.Manual;
    /// <summary>FileExists / AppLaunched 用のパス</summary>
    public string?               Path      { get; set; }
    /// <summary>条件が達成済みかどうか。</summary>
    public bool                  IsAchieved{ get; set; } = false;
    /// <summary>条件が達成された日時。</summary>
    public DateTime?             AchievedAt{ get; set; }
}

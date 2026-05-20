using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// アプリ共通ロガー（シングルトン）。
/// ログは %AppData%\TKer\logs\ に出力される。
/// フォーマット: yyyy/MM/dd HH:mm:ss [LEVEL] [画面名] [機能名] メッセージ
/// </summary>
public class AppLogger
{
    // ── シングルトン ─────────────────────────────────────
    private static AppLogger? _instance;
    /// <summary>シングルトンインスタンスを返す。</summary>
    public  static AppLogger  Instance => _instance ??= new AppLogger();

    // ── フィールド ───────────────────────────────────────
    private static readonly string LOG_DIR =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "TKer", "logs");

    private readonly object _lock = new();
    private string _currentLogFile = "";
    private DateTime _currentFileDate = DateTime.MinValue;

    // インメモリバッファ（直近 2000 件）
    private readonly List<LogEntry> _entries = new();
    private const int MAX_MEMORY_ENTRIES = 2000;

    private LogRotationSettings _rotation = new();

    /// <summary>新規ログエントリ追加時に発火（UIリアルタイム更新用）</summary>
    public event Action<LogEntry>? EntryAdded;

    // ── 初期化 ────────────────────────────────────────────
    /// <summary>ログディレクトリを作成してログローテーションを確認する。</summary>
    private AppLogger()
    {
        Directory.CreateDirectory(LOG_DIR);
        RotateIfNeeded();
    }

    /// <summary>ログローテーション設定を更新して再確認する。</summary>
    public void Configure(LogRotationSettings settings)
    {
        _rotation = settings;
        RotateIfNeeded();
    }

    // ── ログ出力 API ──────────────────────────────────────
    /// <summary>DEBUG レベルのログを出力する。</summary>
    public void Debug(string screen, string func, string msg)
        => Write(AppLogLevel.DEBUG, screen, func, msg);

    /// <summary>INFO レベルのログを出力する。</summary>
    public void Info(string screen, string func, string msg)
        => Write(AppLogLevel.INFO, screen, func, msg);

    /// <summary>WARN レベルのログを出力する。</summary>
    public void Warn(string screen, string func, string msg)
        => Write(AppLogLevel.WARN, screen, func, msg);

    /// <summary>ERROR レベルのログを出力する。例外情報も記録できる。</summary>
    public void Error(string screen, string func, string msg, Exception? ex = null)
        => Write(AppLogLevel.ERROR, screen, func, msg, ex?.ToString());

    /// <summary>FATAL レベルのログを出力する。例外情報も記録できる。</summary>
    public void Fatal(string screen, string func, string msg, Exception? ex = null)
        => Write(AppLogLevel.FATAL, screen, func, msg, ex?.ToString());

    // ── 読み取り ─────────────────────────────────────────
    /// <summary>インメモリの全ログエントリを返す。</summary>
    public IReadOnlyList<LogEntry> GetAll()
    {
        lock (_lock) { return _entries.ToList(); }
    }

    /// <summary>条件でフィルタリングしたログエントリを返す。</summary>
    public IReadOnlyList<LogEntry> Filter(
        string? keyword = null,
        AppLogLevel? minLevel = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        lock (_lock)
        {
            return _entries.Where(e =>
                (minLevel == null || e.Level >= minLevel) &&
                (from == null || e.Timestamp >= from) &&
                (to   == null || e.Timestamp <= to) &&
                (string.IsNullOrEmpty(keyword) ||
                 e.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 e.ScreenName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 e.FunctionName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }
    }

    // ── 内部書き込み ──────────────────────────────────────
    /// <summary>ログエントリをインメモリバッファとファイルに書き込む。</summary>
    private void Write(AppLogLevel level, string screen, string func,
                       string message, string? stackTrace = null)
    {
        var entry = new LogEntry
        {
            Timestamp    = DateTime.Now,
            Level        = level,
            ScreenName   = screen,
            FunctionName = func,
            Message      = message,
            StackTrace   = stackTrace
        };

        lock (_lock)
        {
            // インメモリ追加
            _entries.Add(entry);
            if (_entries.Count > MAX_MEMORY_ENTRIES)
                _entries.RemoveAt(0);

            // ファイル書き込み
            try
            {
                EnsureCurrentFile();
                File.AppendAllText(_currentLogFile,
                    entry.FormattedLine + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { /* ログ自体のエラーは無視 */ }

            // ローテーションチェック
            RotateIfNeeded();
        }

        // UI 通知（lock 外）
        try { EntryAdded?.Invoke(entry); }
        catch { /* 無視 */ }
    }

    // ── ファイル管理 ─────────────────────────────────────
    /// <summary>現在の日付に対応するログファイルパスを確保する。</summary>
    private void EnsureCurrentFile()
    {
        var today = DateTime.Today;
        if (_currentLogFile == "" || today != _currentFileDate)
        {
            _currentFileDate = today;
            _currentLogFile  = Path.Combine(LOG_DIR,
                $"TKer_{today:yyyyMMdd}.log");
        }
    }

    /// <summary>ローテーション設定に基づいて古いログファイルを整理する。</summary>
    private void RotateIfNeeded()
    {
        if (!_rotation.Enabled) return;

        var logFiles = Directory.GetFiles(LOG_DIR, "TKer_*.log")
                                .OrderByDescending(f => f)
                                .ToList();

        // サイズチェック
        if (_rotation.Period == "size" && _currentLogFile != "")
        {
            try
            {
                var fi = new FileInfo(_currentLogFile);
                if (fi.Exists && fi.Length > _rotation.MaxFileSizeMB * 1024L * 1024L)
                {
                    ArchiveOrDelete(_currentLogFile);
                    _currentLogFile = "";
                    EnsureCurrentFile();
                    logFiles = Directory.GetFiles(LOG_DIR, "TKer_*.log")
                                        .OrderByDescending(f => f).ToList();
                }
            }
            catch { /* 無視 */ }
        }

        // 古いファイルの整理
        if (logFiles.Count > _rotation.MaxFiles)
        {
            foreach (var old in logFiles.Skip(_rotation.MaxFiles))
                ArchiveOrDelete(old);
        }
    }

    /// <summary>ログファイルをアーカイブまたは削除する。</summary>
    private void ArchiveOrDelete(string filePath)
    {
        try
        {
            if (_rotation.OnRotate == "backup")
            {
                var archivePath = filePath.Replace(".log", ".log.bak");
                if (File.Exists(archivePath)) File.Delete(archivePath);
                File.Move(filePath, archivePath);
            }
            else
            {
                File.Delete(filePath);
            }
        }
        catch { /* 無視 */ }
    }

    // ── 過去ログファイルの読み込み ───────────────────────
    /// <summary>指定日数分の過去ログをファイルから読み込んで返す。</summary>
    public List<LogEntry> LoadHistoricalLogs(int days = 7)
    {
        var result = new List<LogEntry>();
        for (int i = 0; i < days; i++)
        {
            var date = DateTime.Today.AddDays(-i);
            var file = Path.Combine(LOG_DIR, $"TKer_{date:yyyyMMdd}.log");
            if (!File.Exists(file)) continue;

            try
            {
                foreach (var line in File.ReadLines(file, Encoding.UTF8))
                {
                    var e = ParseLine(line);
                    if (e != null) result.Add(e);
                }
            }
            catch { /* 読み取りエラーは無視 */ }
        }
        return result.OrderByDescending(e => e.Timestamp).ToList();
    }

    /// <summary>ログファイルの 1 行をパースして LogEntry に変換する。</summary>
    private static LogEntry? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            // フォーマット: "yyyy/MM/dd HH:mm:ss [LEVEL] [Screen] [Func] Message"
            var parts = line.Split(' ', 4);
            if (parts.Length < 4) return null;
            if (!DateTime.TryParse($"{parts[0]} {parts[1]}", out var ts)) return null;
            var level = parts[2].Trim('[', ']', ' ') switch
            {
                "DEBUG" => AppLogLevel.DEBUG,
                "WARN"  => AppLogLevel.WARN,
                "ERROR" => AppLogLevel.ERROR,
                "FATAL" => AppLogLevel.FATAL,
                _       => AppLogLevel.INFO
            };
            return new LogEntry { Timestamp = ts, Level = level, Message = parts[3] };
        }
        catch { return null; }
    }
}

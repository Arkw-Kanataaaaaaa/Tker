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
    public  static AppLogger  Instance => _instance ??= new AppLogger();

    // ── フィールド ───────────────────────────────────────
    private static readonly string LogDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "TKer", "logs");

    private readonly object _lock = new();
    private string _currentLogFile = "";
    private DateTime _currentFileDate = DateTime.MinValue;

    // インメモリバッファ（直近 2000 件）
    private readonly List<LogEntry> _entries = new();
    private const int MaxMemoryEntries = 2000;

    private LogRotationSettings _rotation = new();

    /// <summary>新規ログエントリ追加時に発火（UIリアルタイム更新用）</summary>
    public event Action<LogEntry>? EntryAdded;

    // ── 初期化 ────────────────────────────────────────────
    private AppLogger()
    {
        Directory.CreateDirectory(LogDir);
        RotateIfNeeded();
    }

    public void Configure(LogRotationSettings settings)
    {
        _rotation = settings;
        RotateIfNeeded();
    }

    // ── ログ出力 API ──────────────────────────────────────
    public void Debug(string screen, string func, string msg)
        => Write(AppLogLevel.DEBUG, screen, func, msg);

    public void Info(string screen, string func, string msg)
        => Write(AppLogLevel.INFO, screen, func, msg);

    public void Warn(string screen, string func, string msg)
        => Write(AppLogLevel.WARN, screen, func, msg);

    public void Error(string screen, string func, string msg, Exception? ex = null)
        => Write(AppLogLevel.ERROR, screen, func, msg, ex?.ToString());

    public void Fatal(string screen, string func, string msg, Exception? ex = null)
        => Write(AppLogLevel.FATAL, screen, func, msg, ex?.ToString());

    // ── 読み取り ─────────────────────────────────────────
    public IReadOnlyList<LogEntry> GetAll()
    {
        lock (_lock) { return _entries.ToList(); }
    }

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
            if (_entries.Count > MaxMemoryEntries)
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
    private void EnsureCurrentFile()
    {
        var today = DateTime.Today;
        if (_currentLogFile == "" || today != _currentFileDate)
        {
            _currentFileDate = today;
            _currentLogFile  = Path.Combine(LogDir,
                $"TKer_{today:yyyyMMdd}.log");
        }
    }

    private void RotateIfNeeded()
    {
        if (!_rotation.Enabled) return;

        var logFiles = Directory.GetFiles(LogDir, "TKer_*.log")
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
                    logFiles = Directory.GetFiles(LogDir, "TKer_*.log")
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
    public List<LogEntry> LoadHistoricalLogs(int days = 7)
    {
        var result = new List<LogEntry>();
        for (int i = 0; i < days; i++)
        {
            var date = DateTime.Today.AddDays(-i);
            var file = Path.Combine(LogDir, $"TKer_{date:yyyyMMdd}.log");
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

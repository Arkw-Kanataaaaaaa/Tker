using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// 60秒ごとにフォルダ整合性チェックを実行するバックグラウンドサービス。
/// タスクに紐づくフォルダが存在するかどうか、フォルダ名が正しいかを確認する。
/// </summary>
public class FolderIntegrityService
{
    private readonly ProjectService _projectService;
    private readonly AppLogger      _logger = AppLogger.Instance;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;

    /// <summary>整合性問題が見つかった時に発火 (taskId, 問題の説明)</summary>
    public event Action<string, string>? IntegrityIssueFound;

    /// <summary>整合性チェック完了時に発火 (問題件数)</summary>
    public event Action<int>? CheckCompleted;

    /// <summary>ProjectService を受け取って初期化する。</summary>
    public FolderIntegrityService(ProjectService projectService)
    {
        _projectService = projectService;
    }

    // ── 開始 / 停止 ──────────────────────────────────────
    /// <summary>バックグラウンドでの整合性チェックを開始する。</summary>
    public void Start()
    {
        if (_backgroundTask != null && !_backgroundTask.IsCompleted) return;

        _cts = new CancellationTokenSource();
        _backgroundTask = RunAsync(_cts.Token);
        _logger.Info("FolderIntegrityService", "Start", "フォルダ整合性チェック開始 (60秒周期)");
    }

    /// <summary>バックグラウンドでの整合性チェックを停止する。</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _logger.Info("FolderIntegrityService", "Stop", "フォルダ整合性チェック停止");
    }

    // ── バックグラウンドループ ────────────────────────────
    /// <summary>60秒周期で整合性チェックを繰り返す非同期ループ。</summary>
    private async Task RunAsync(CancellationToken ct)
    {
        // 起動直後は少し待つ
        await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var issues = await Task.Run(() => PerformCheck(), ct)
                                       .ConfigureAwait(false);

                CheckCompleted?.Invoke(issues);

                if (issues > 0)
                    _logger.Warn("FolderIntegrityService", "RunAsync",
                        $"整合性チェック完了: {issues} 件の問題");
                else
                    _logger.Debug("FolderIntegrityService", "RunAsync",
                        "整合性チェック完了: 問題なし");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.Error("FolderIntegrityService", "RunAsync",
                    "整合性チェック中に例外", ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
        }
    }

    // ── 実際のチェック処理 ───────────────────────────────
    /// <summary>現在のプロジェクトの全タスク・カテゴリのフォルダ整合性を検査し、問題件数を返す。</summary>
    private int PerformCheck()
    {
        var project = _projectService.CurrentProject;
        if (project == null) return 0;

        int issues = 0;
        var reports = new List<(string TaskId, string Desc)>();

        foreach (var task in project.Tasks)
        {
            if (string.IsNullOrEmpty(task.FolderPath)) continue;

            // 1) フォルダが存在するか
            if (!Directory.Exists(task.FolderPath))
            {
                var msg = $"タスク「{task.Name}」のフォルダが見つかりません: {task.FolderPath}";
                reports.Add((task.Id, msg));
                issues++;
                continue;
            }

            // 2) フォルダ名が期待値と一致するか (Id_NameShort)
            var expectedName = task.FolderName; // "{Id}_{NameShort}"
            var actualName   = Path.GetFileName(task.FolderPath);
            if (!string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                var msg = $"タスク「{task.Name}」フォルダ名不一致: 期待={expectedName}, 実際={actualName}";
                reports.Add((task.Id, msg));
                issues++;
            }
        }

        // カテゴリフォルダチェック
        foreach (var cat in project.Categories)
        {
            if (string.IsNullOrEmpty(cat.FolderPath)) continue;
            if (!Directory.Exists(cat.FolderPath))
            {
                var msg = $"カテゴリ「{cat.Name}」のフォルダが見つかりません: {cat.FolderPath}";
                _logger.Warn("FolderIntegrityService", "PerformCheck", msg);
                issues++;
            }
        }

        // 通知
        foreach (var (taskId, desc) in reports)
        {
            _logger.Warn("FolderIntegrityService", "PerformCheck", desc);
            IntegrityIssueFound?.Invoke(taskId, desc);
        }

        return issues;
    }
}

using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TKer.Services;

/// <summary>フォルダ操作の種別を表す列挙型。</summary>
public enum FolderOpKind { Create, Rename, Move, Delete, Custom }

/// <summary>キューに積む単一のフォルダ操作を表すクラス。</summary>
public sealed class FolderOperation
{
    /// <summary>操作の種別。</summary>
    public FolderOpKind Kind       { get; init; } = FolderOpKind.Custom;
    /// <summary>操作のラベル（エラー通知で使用）。</summary>
    public string       Label      { get; init; } = "";
    /// <summary>操作元パス。</summary>
    public string?      SourcePath { get; init; }
    /// <summary>操作先パス。</summary>
    public string?      DestPath   { get; init; }
    /// <summary>複合操作（Custom種別）。例外は呼び出し元がキャッチする。</summary>
    public Action?      Custom     { get; init; }
}

/// <summary>
/// フォルダ操作を非同期・直列で実行するキュー。
/// 失敗時は OperationFailed イベントで通知する。
/// キュー件数は PendingCountChanged で通知する。
/// </summary>
public sealed class FolderOperationQueue : IDisposable
{
    private readonly Channel<FolderOperation> _ch =
        Channel.CreateUnbounded<FolderOperation>(new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private int _pendingCount = 0;

    /// <summary>バックグラウンドスレッドから呼ばれる。ラベル + 例外を渡す。</summary>
    public event Action<string, Exception>? OperationFailed;

    /// <summary>キュー残件数が変化したとき通知（バックグラウンドスレッドから）。</summary>
    public event Action<int>? PendingCountChanged;

    /// <summary>現在のキュー残件数。</summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <summary>バックグラウンドワーカーを起動して初期化する。</summary>
    public FolderOperationQueue() => _worker = Task.Run(RunAsync);

    /// <summary>操作をキューに追加する（即座に返る）。</summary>
    public void Enqueue(FolderOperation op)
    {
        int n = Interlocked.Increment(ref _pendingCount);
        PendingCountChanged?.Invoke(n);
        _ch.Writer.TryWrite(op);
    }

    /// <summary>キューからフォルダ操作を順に取り出して実行する非同期ループ。</summary>
    private async Task RunAsync()
    {
        await foreach (var op in _ch.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                if (op.Custom != null)
                {
                    op.Custom();
                }
                else
                {
                    switch (op.Kind)
                    {
                        case FolderOpKind.Create:
                            if (!string.IsNullOrEmpty(op.SourcePath))
                                Directory.CreateDirectory(op.SourcePath);
                            break;

                        case FolderOpKind.Rename:
                        case FolderOpKind.Move:
                            if (!string.IsNullOrEmpty(op.SourcePath) &&
                                !string.IsNullOrEmpty(op.DestPath)   &&
                                Directory.Exists(op.SourcePath))
                                Directory.Move(op.SourcePath, op.DestPath);
                            break;

                        case FolderOpKind.Delete:
                            if (!string.IsNullOrEmpty(op.SourcePath) &&
                                Directory.Exists(op.SourcePath))
                                Directory.Delete(op.SourcePath, recursive: true);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                OperationFailed?.Invoke(op.Label, ex);
            }
            finally
            {
                int remaining = Interlocked.Decrement(ref _pendingCount);
                if (remaining < 0) remaining = Interlocked.Exchange(ref _pendingCount, 0);
                PendingCountChanged?.Invoke(remaining);
            }
        }
    }

    /// <summary>キャンセルトークンをキャンセルし、ワーカーの終了を待機してリソースを解放する。</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _ch.Writer.TryComplete();
        try { _worker.Wait(3000); } catch { }
        _cts.Dispose();
    }
}

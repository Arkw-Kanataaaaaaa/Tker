using System;
using System.Diagnostics;

namespace TKer.Helpers;

/// <summary>
/// エクスプローラー連携など、シェル操作のヘルパー。
/// </summary>
public static class ShellHelper
{
    /// <summary>
    /// 指定パスをエクスプローラーで開く。パスは必ず引用符で囲み、
    /// スペースを含むパスでも誤った場所が開かないようにする。失敗しても例外を投げない。
    /// </summary>
    public static void OpenInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellHelper] OpenInExplorer 失敗: {ex.Message}");
        }
    }
}

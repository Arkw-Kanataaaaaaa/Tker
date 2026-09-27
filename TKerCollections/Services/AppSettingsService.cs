using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// アプリ全体設定（コレクションのファイルパス一覧のみ）を管理するサービス。
/// %AppData%\TKerCollections\settings.json に保存される。
/// </summary>
public class AppSettingsService
{
    private static readonly string SETTINGS_DIR =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TKerCollections");
    private static readonly string SETTINGS_FILE =
        Path.Combine(SETTINGS_DIR, "settings.json");

    private AppSettings _settings;

    /// <summary>設定ファイルを読み込んでサービスを初期化する。</summary>
    public AppSettingsService()
    {
        _settings = Load();
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

    // ── ロード / セーブ ───────────────────────────
    /// <summary>設定ファイルを読み込んで返す（ファイル不在・破損時は新規設定を返す）。</summary>
    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SETTINGS_FILE))
            {
                var json = File.ReadAllText(SETTINGS_FILE);
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* 初回起動や破損時は新規作成 */ }
        return new AppSettings();
    }

    /// <summary>現在の設定をJSONファイルに書き出す。</summary>
    private void Save()
    {
        Directory.CreateDirectory(SETTINGS_DIR);
        var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
        File.WriteAllText(SETTINGS_FILE, json);
    }
}

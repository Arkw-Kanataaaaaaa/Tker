using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// コレクションデータを ドキュメント\TKer_CL\コレクション名_collection.json で管理し、
/// ファイルパスは settings.json の CollectionFilePaths で追跡するサービス。
/// </summary>
public class CollectionService
{
    private static readonly string DIR =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TKer_CL");

    private readonly AppSettingsService _appSettings;
    private List<Collection>            _collections;

    /// <summary>AppSettingsService を受け取り、登録済みパスからコレクションを読み込む。</summary>
    public CollectionService(AppSettingsService appSettings)
    {
        _appSettings = appSettings;
        _collections = LoadAll();
    }

    /// <summary>現在のコレクション一覧を返す。</summary>
    public IReadOnlyList<Collection> Collections => _collections;

    // ── CRUD ─────────────────────────────────────────────

    /// <summary>新しいコレクションを追加してファイルと settings に登録する。</summary>
    public void Add(Collection c)
    {
        _collections.Add(c);
        var path = Persist(c);
        _appSettings.AddCollectionFilePath(path);
    }

    /// <summary>既存のコレクションを更新してファイルを書き直す。名前変更時は旧ファイルを削除する。</summary>
    public void Update(Collection c)
    {
        var idx = _collections.FindIndex(x => x.Id == c.Id);
        if (idx < 0) return;

        var old     = _collections[idx];
        var oldPath = GetFilePath(old);

        if (old.Name != c.Name)
        {
            _appSettings.RemoveCollectionFilePath(oldPath);
            TryDeleteFile(oldPath);
        }

        _collections[idx] = c;
        var newPath = Persist(c);
        _appSettings.AddCollectionFilePath(newPath);
    }

    /// <summary>指定 ID のコレクションを削除してファイルと settings から除去する。</summary>
    public void Delete(string id)
    {
        var col = _collections.FirstOrDefault(x => x.Id == id);
        if (col != null)
        {
            var path = GetFilePath(col);
            _appSettings.RemoveCollectionFilePath(path);
            TryDeleteFile(path);
        }
        _collections.RemoveAll(x => x.Id == id);
    }

    // ── 内部ヘルパー ─────────────────────────────────────

    private List<Collection> LoadAll()
    {
        try { Directory.CreateDirectory(DIR); } catch { }

        var result     = new List<Collection>();
        var validPaths = new List<string>();

        foreach (var path in _appSettings.CollectionFilePaths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var col = JsonConvert.DeserializeObject<Collection>(File.ReadAllText(path));
                if (col != null) { result.Add(col); validPaths.Add(path); }
            }
            catch
            {
                validPaths.Add(path);
            }
        }

        if (validPaths.Count != _appSettings.CollectionFilePaths.Count)
            _appSettings.SyncCollectionFilePaths(validPaths);

        return result;
    }

    private static string GetFilePath(Collection c)
    {
        var invalid  = Path.GetInvalidFileNameChars();
        var safeName = new string(c.Name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        if (string.IsNullOrWhiteSpace(safeName)) safeName = c.Id;
        return Path.Combine(DIR, $"{safeName}_collection.json");
    }

    private static string Persist(Collection c)
    {
        try
        {
            Directory.CreateDirectory(DIR);
            var path = GetFilePath(c);
            File.WriteAllText(path, JsonConvert.SerializeObject(c, Formatting.Indented));
            return path;
        }
        catch { return GetFilePath(c); }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

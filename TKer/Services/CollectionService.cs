using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// コレクションデータを ドキュメント\TKer_CL\コレクション名_collection.json で管理するサービス。
/// </summary>
public class CollectionService
{
    private static readonly string DIR =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TKer_CL");

    private List<Collection> _collections;

    /// <summary>ディレクトリをスキャンしてコレクションを読み込んで初期化する。</summary>
    public CollectionService() => _collections = LoadAll();

    /// <summary>現在のコレクション一覧を返す。</summary>
    public IReadOnlyList<Collection> Collections => _collections;

    // ── CRUD ─────────────────────────────────────────────

    /// <summary>新しいコレクションを追加してファイルに保存する。</summary>
    public void Add(Collection c)
    {
        _collections.Add(c);
        Persist(c);
    }

    /// <summary>既存のコレクションを更新してファイルに保存する。</summary>
    public void Update(Collection c)
    {
        var idx = _collections.FindIndex(x => x.Id == c.Id);
        if (idx < 0) return;

        var old = _collections[idx];
        if (old.Name != c.Name)
            TryDeleteFile(GetFilePath(old));

        _collections[idx] = c;
        Persist(c);
    }

    /// <summary>指定 ID のコレクションを削除してファイルを消去する。</summary>
    public void Delete(string id)
    {
        var col = _collections.FirstOrDefault(x => x.Id == id);
        if (col != null) TryDeleteFile(GetFilePath(col));
        _collections.RemoveAll(x => x.Id == id);
    }

    // ── 内部ヘルパー ─────────────────────────────────────

    private static List<Collection> LoadAll()
    {
        var result = new List<Collection>();
        try
        {
            Directory.CreateDirectory(DIR);
            foreach (var file in Directory.GetFiles(DIR, "*_collection.json"))
            {
                try
                {
                    var col = JsonConvert.DeserializeObject<Collection>(File.ReadAllText(file));
                    if (col != null) result.Add(col);
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    private static string GetFilePath(Collection c)
    {
        var invalid  = Path.GetInvalidFileNameChars();
        var safeName = new string(c.Name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        if (string.IsNullOrWhiteSpace(safeName)) safeName = c.Id;
        return Path.Combine(DIR, $"{safeName}_collection.json");
    }

    private static void Persist(Collection c)
    {
        try
        {
            Directory.CreateDirectory(DIR);
            File.WriteAllText(GetFilePath(c),
                JsonConvert.SerializeObject(c, Formatting.Indented));
        }
        catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

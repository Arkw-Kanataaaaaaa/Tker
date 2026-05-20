using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// コレクションデータを %AppData%\TKer\collections.json で管理するサービス。
/// </summary>
public class CollectionService
{
    private static readonly string DataFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "TKer", "collections.json");

    private List<Collection> _collections;

    public CollectionService() => _collections = Load();

    public IReadOnlyList<Collection> Collections => _collections;

    // ── CRUD ──────────────────────────────────

    public void Add(Collection c)
    {
        _collections.Add(c);
        Save();
    }

    public void Update(Collection c)
    {
        var idx = _collections.FindIndex(x => x.Id == c.Id);
        if (idx >= 0) { _collections[idx] = c; Save(); }
    }

    public void Delete(string id)
    {
        _collections.RemoveAll(x => x.Id == id);
        Save();
    }

    // ── 永続化 ────────────────────────────────

    private List<Collection> Load()
    {
        try
        {
            if (File.Exists(DataFile))
            {
                var json = File.ReadAllText(DataFile);
                return JsonConvert.DeserializeObject<List<Collection>>(json) ?? new();
            }
        }
        catch { /* 破損時は新規作成 */ }
        return new();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataFile)!);
        File.WriteAllText(DataFile, JsonConvert.SerializeObject(_collections, Formatting.Indented));
    }
}

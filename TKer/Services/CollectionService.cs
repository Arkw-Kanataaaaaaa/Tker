using System;
using System.Collections.Generic;
using TKer.Helpers;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// コレクションデータを %AppData%\TKer\collections.json で管理するサービス。
/// </summary>
public class CollectionService
{
    private const string FileName = "collections.json";

    private List<Collection> _collections;

    public CollectionService() => _collections = JsonFileStore.Load<List<Collection>>(FileName);

    public IReadOnlyList<Collection> Collections => _collections;

    // ── CRUD ─────────────────────────────────────────────
    public void Add(Collection c)
    {
        _collections.Add(c);
        JsonFileStore.Save(FileName, _collections);
    }

    public void Update(Collection c)
    {
        var idx = _collections.FindIndex(x => x.Id == c.Id);
        if (idx >= 0)
        {
            _collections[idx] = c;
            JsonFileStore.Save(FileName, _collections);
        }
    }

    public void Delete(string id)
    {
        _collections.RemoveAll(x => x.Id == id);
        JsonFileStore.Save(FileName, _collections);
    }
}

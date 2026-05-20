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
    private const string FILE_NAME = "collections.json";

    private List<Collection> _collections;

    /// <summary>JSON ファイルからコレクションを読み込んで初期化する。</summary>
    public CollectionService() => _collections = JsonFileStore.Load<List<Collection>>(FILE_NAME);

    /// <summary>現在のコレクション一覧を返す。</summary>
    public IReadOnlyList<Collection> Collections => _collections;

    // ── CRUD ─────────────────────────────────────────────
    /// <summary>新しいコレクションを追加して保存する。</summary>
    public void Add(Collection c)
    {
        _collections.Add(c);
        JsonFileStore.Save(FILE_NAME, _collections);
    }

    /// <summary>既存のコレクションを更新して保存する。</summary>
    public void Update(Collection c)
    {
        var idx = _collections.FindIndex(x => x.Id == c.Id);
        if (idx >= 0)
        {
            _collections[idx] = c;
            JsonFileStore.Save(FILE_NAME, _collections);
        }
    }

    /// <summary>指定 ID のコレクションを削除して保存する。</summary>
    public void Delete(string id)
    {
        _collections.RemoveAll(x => x.Id == id);
        JsonFileStore.Save(FILE_NAME, _collections);
    }
}

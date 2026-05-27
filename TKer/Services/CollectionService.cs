using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// コレクションデータを管理するサービス。
/// 文字列形式は ドキュメント\TKerEmptyLibrary\Collection 配下に、
/// ファイル形式（保存先フォルダ指定あり）は 指定フォルダ\コレクション名\ 配下に
/// "コレクション名_collection.json" として保持する。ファイルパスは settings.json の
/// CollectionFilePaths で追跡する。
/// </summary>
public class CollectionService
{
    /// <summary>フォルダ管理しない（文字列形式）コレクションの保存先。</summary>
    private static readonly string NoFolderDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     "TKerEmptyLibrary", "Collection");

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

    /// <summary>
    /// 新しいコレクションを追加してファイルと settings に登録する。
    /// ファイル形式の場合は parentFolder 直下に "コレクション名" フォルダを生成し、その中に保持する。
    /// </summary>
    /// <param name="c">追加するコレクション（FolderPath はサービス側で設定する）。</param>
    /// <param name="parentFolder">ファイル形式時に選択された親フォルダ。</param>
    public void Add(Collection c, string parentFolder)
    {
        string jsonPath;

        if (c.ItemFormat == "ファイル" && !string.IsNullOrWhiteSpace(parentFolder))
        {
            var sub = Path.Combine(parentFolder, ProjectService.SanitizeFileName(c.Name));
            Directory.CreateDirectory(sub);
            c.FolderPath = sub;
            jsonPath = JsonPathFor(c);
        }
        else
        {
            Directory.CreateDirectory(NoFolderDir);
            c.FolderPath = "";
            jsonPath = JsonPathFor(c);
        }

        _collections.Add(c);
        Write(c, jsonPath);
        _appSettings.AddCollectionFilePath(jsonPath);
    }

    /// <summary>
    /// 読み込み（インポート）したコレクションを、ファイルを移動せずそのまま登録する。
    /// </summary>
    public void AddImported(Collection c, string jsonPath)
    {
        _collections.Add(c);
        _appSettings.AddCollectionFilePath(jsonPath);
    }

    /// <summary>
    /// 既存のコレクションを更新する。名前・形式・保存先フォルダの変更に応じてフォルダ／ファイルを再配置する。
    /// ファイル形式で親フォルダが変わった場合は既存フォルダを新しい親フォルダ配下へ移動する。
    /// </summary>
    /// <param name="c">更新後の内容（FolderPath はサービス側で設定する）。</param>
    /// <param name="parentFolder">ファイル形式時に選択された親フォルダ。</param>
    /// <param name="old">変更前の状態（Name / ItemFormat / FolderPath を参照）。</param>
    public void Update(Collection c, string parentFolder, Collection old)
    {
        var idx = _collections.FindIndex(x => x.Id == c.Id);
        if (idx < 0) return;

        var oldJson = JsonPathFor(old);
        string newJson;

        if (c.ItemFormat == "ファイル" && !string.IsNullOrWhiteSpace(parentFolder))
        {
            var newSub = Path.Combine(parentFolder, ProjectService.SanitizeFileName(c.Name));

            if (old.ItemFormat == "ファイル" && !string.IsNullOrWhiteSpace(old.FolderPath)
                && Directory.Exists(old.FolderPath) && !PathsEqual(old.FolderPath, newSub))
            {
                // 既存フォルダを新しい親フォルダ配下へ移動（リネーム含む）
                Directory.CreateDirectory(parentFolder);
                if (Directory.Exists(newSub))
                    throw new IOException($"移動先に同名フォルダが既に存在します:\n{newSub}");
                Directory.Move(old.FolderPath, newSub);
            }
            else
            {
                Directory.CreateDirectory(newSub);
            }

            c.FolderPath = newSub;
            newJson = JsonPathFor(c);

            // 旧 json の現在位置（移動後）を解決して新名へリネーム
            var currentOld = old.ItemFormat == "ファイル"
                ? Path.Combine(newSub, Path.GetFileName(oldJson))
                : oldJson;
            if (File.Exists(currentOld) && !PathsEqual(currentOld, newJson))
            {
                if (File.Exists(newJson)) File.Delete(newJson);
                File.Move(currentOld, newJson);
            }
        }
        else
        {
            Directory.CreateDirectory(NoFolderDir);
            c.FolderPath = "";
            newJson = JsonPathFor(c);
            if (File.Exists(oldJson) && !PathsEqual(oldJson, newJson))
            {
                if (File.Exists(newJson)) File.Delete(newJson);
                File.Move(oldJson, newJson);
            }
        }

        _collections[idx] = c;

        if (!PathsEqual(oldJson, newJson))
            _appSettings.RemoveCollectionFilePath(oldJson);
        Write(c, newJson);
        _appSettings.AddCollectionFilePath(newJson);
    }

    /// <summary>
    /// コレクションの内容（アイテム等）を現在の保存先へ書き直す。フォルダの移動・改名は行わない。
    /// </summary>
    public void Save(Collection c)
    {
        var idx = _collections.FindIndex(x => x.Id == c.Id);
        if (idx >= 0) _collections[idx] = c;

        var jsonPath = JsonPathFor(c);
        Write(c, jsonPath);
        _appSettings.AddCollectionFilePath(jsonPath);
    }

    /// <summary>指定 ID のコレクションを削除してファイルと settings から除去する。</summary>
    public void Delete(string id)
    {
        var col = _collections.FirstOrDefault(x => x.Id == id);
        if (col != null)
        {
            var path = JsonPathFor(col);
            _appSettings.RemoveCollectionFilePath(path);
            TryDeleteFile(path);
        }
        _collections.RemoveAll(x => x.Id == id);
    }

    // ── 内部ヘルパー ─────────────────────────────────────

    private List<Collection> LoadAll()
    {
        try { Directory.CreateDirectory(NoFolderDir); } catch { }

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

    /// <summary>コレクションの現在の保存先に基づく JSON ファイルパスを返す。</summary>
    private static string JsonPathFor(Collection c)
    {
        var safe     = ProjectService.SanitizeFileName(c.Name);
        var fileName = $"{safe}_collection.json";
        return c.ItemFormat == "ファイル" && !string.IsNullOrWhiteSpace(c.FolderPath)
            ? Path.Combine(c.FolderPath, fileName)
            : Path.Combine(NoFolderDir, fileName);
    }

    private static void Write(Collection c, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(c, Formatting.Indented));
        }
        catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

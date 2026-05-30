using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// ロードマップ項目の CRUD と JSON 永続化を担うサービス。
/// %AppData%\TKer\roadmap.json に保存される。
/// </summary>
public class RoadmapService
{
    private static readonly string DATA_DIR =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TKer");
    private static readonly string DATA_FILE =
        Path.Combine(DATA_DIR, "roadmap.json");

    private List<RoadmapItem> _items;

    /// <summary>保存済み項目を読み込んでサービスを初期化する。</summary>
    public RoadmapService() { _items = Load(); }

    /// <summary>全項目を表示順（Version → Order → TargetDate）で返す。</summary>
    public IReadOnlyList<RoadmapItem> All =>
        _items.OrderBy(i => i.Version)
              .ThenBy(i => i.Order)
              .ThenBy(i => i.TargetDate ?? DateTime.MaxValue)
              .ToList();

    /// <summary>新規項目を追加して保存する。</summary>
    public RoadmapItem Add(string title, string description, string version,
                           string status, DateTime? targetDate)
    {
        var item = new RoadmapItem
        {
            Title       = title,
            Description = description,
            Version     = version,
            Status      = status,
            TargetDate  = targetDate,
            Order       = NextOrder(version)
        };
        _items.Add(item);
        Save();
        return item;
    }

    /// <summary>既存項目の更新を保存する（UpdatedAt を現在時刻に更新する）。</summary>
    public void Update(RoadmapItem item)
    {
        item.UpdatedAt = DateTime.Now;
        Save();
    }

    /// <summary>指定 ID の項目を削除して保存する。</summary>
    public void Delete(string id)
    {
        _items.RemoveAll(i => i.Id == id);
        Save();
    }

    /// <summary>指定バージョン内での次の Order 値を返す。</summary>
    private int NextOrder(string version) =>
        _items.Where(i => i.Version == version)
              .Select(i => i.Order)
              .DefaultIfEmpty(0)
              .Max() + 1;

    /// <summary>JSON ファイルから項目を読み込んで返す。失敗時は空リスト。</summary>
    private List<RoadmapItem> Load()
    {
        try
        {
            if (File.Exists(DATA_FILE))
                return JsonConvert.DeserializeObject<List<RoadmapItem>>(File.ReadAllText(DATA_FILE))
                       ?? new List<RoadmapItem>();
        }
        catch { /* 読み込み失敗時は新規 */ }
        return new List<RoadmapItem>();
    }

    /// <summary>現在の項目一覧を JSON で書き出す。</summary>
    private void Save()
    {
        Directory.CreateDirectory(DATA_DIR);
        var json = JsonConvert.SerializeObject(_items, Formatting.Indented);
        File.WriteAllText(DATA_FILE, json);
    }
}

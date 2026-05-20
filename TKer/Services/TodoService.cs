using System;
using System.Collections.Generic;
using System.Linq;
using TKer.Helpers;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// ToDo アイテムの CRUD を管理するサービス。
/// %AppData%\TKer\todos.json に保存される。
/// </summary>
public class TodoService
{
    private const string FILE_NAME = "todos.json";

    private List<TodoItem> _items;

    public event EventHandler? DataChanged;

    /// <summary>JSON ファイルから ToDo アイテムを読み込んで初期化する。</summary>
    public TodoService() => _items = JsonFileStore.Load<List<TodoItem>>(FILE_NAME);

    // ── 読み取り ─────────────────────────────────────────
    /// <summary>全 ToDo アイテムを優先度順に返す。</summary>
    public IReadOnlyList<TodoItem> GetAll()
        => _items.OrderBy(t => t.IsCompleted)
                 .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                 .ThenByDescending(t => t.CreatedAt)
                 .ToList();

    /// <summary>指定タスク ID に紐づく ToDo アイテムを返す。</summary>
    public IReadOnlyList<TodoItem> GetByTaskId(string taskId)
        => _items.Where(t => t.LinkedTaskId == taskId)
                 .OrderBy(t => t.IsCompleted)
                 .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                 .ToList();

    /// <summary>タスクに紐づいていない ToDo アイテムを返す。</summary>
    public IReadOnlyList<TodoItem> GetUnlinked()
        => _items.Where(t => t.LinkedTaskId == null)
                 .OrderBy(t => t.IsCompleted)
                 .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                 .ToList();

    /// <summary>指定日付に期限が一致する ToDo アイテムを返す。</summary>
    public IReadOnlyList<TodoItem> GetByDate(DateTime date)
        => _items.Where(t => t.DueDate.HasValue && t.DueDate.Value.Date == date.Date)
                 .ToList();

    // ── 追加 / 更新 / 削除 ───────────────────────────────
    /// <summary>新しい ToDo アイテムを追加して保存する。</summary>
    public TodoItem Add(string title, string? notes = null,
        DateTime? dueDate = null, string color = "#3D7EFF",
        TodoRepeat repeat = TodoRepeat.None, string? linkedTaskId = null,
        string? mapInfo = null)
    {
        var item = new TodoItem
        {
            Title        = title,
            Notes        = notes,
            DueDate      = dueDate,
            Color        = color,
            Repeat       = repeat,
            LinkedTaskId = linkedTaskId,
            MapInfo      = mapInfo,
        };
        _items.Add(item);
        SaveAndNotify();
        return item;
    }

    /// <summary>既存の ToDo アイテムを更新して保存する。</summary>
    public void Update(TodoItem updated)
    {
        var idx = _items.FindIndex(t => t.Id == updated.Id);
        if (idx < 0) return;
        updated.UpdatedAt = DateTime.Now;
        _items[idx] = updated;
        SaveAndNotify();
    }

    /// <summary>指定 ID の ToDo アイテムを削除して保存する。</summary>
    public void Delete(string id)
    {
        if (_items.RemoveAll(t => t.Id == id) > 0)
            SaveAndNotify();
    }

    /// <summary>指定 ID の ToDo アイテムの完了状態を切り替える。</summary>
    public void Toggle(string id)
    {
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item == null) return;

        item.IsCompleted = !item.IsCompleted;
        item.CompletedAt = item.IsCompleted ? DateTime.Now : null;
        item.UpdatedAt   = DateTime.Now;

        if (item.IsCompleted && item.Repeat != TodoRepeat.None && item.DueDate.HasValue)
        {
            item.DueDate     = NextDueDate(item.DueDate.Value, item.Repeat);
            item.IsCompleted = false;
            item.CompletedAt = null;
        }

        SaveAndNotify();
    }

    // ── 繰り返し次期日計算 ───────────────────────────────
    /// <summary>繰り返し設定に基づいて次の期限日を計算する。</summary>
    private static DateTime NextDueDate(DateTime current, TodoRepeat repeat) => repeat switch
    {
        TodoRepeat.Daily   => current.AddDays(1),
        TodoRepeat.Weekday => NextWeekday(current),
        TodoRepeat.Weekly  => current.AddDays(7),
        TodoRepeat.Monthly => current.AddMonths(1),
        TodoRepeat.Yearly  => current.AddYears(1),
        _                  => current
    };

    /// <summary>指定日の翌平日を返す。</summary>
    private static DateTime NextWeekday(DateTime from)
    {
        var next = from.AddDays(1);
        while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
            next = next.AddDays(1);
        return next;
    }

    // ── 統計 ─────────────────────────────────────────────
    /// <summary>全アイテム数を返す。</summary>
    public int TotalCount     => _items.Count;
    /// <summary>完了済みアイテム数を返す。</summary>
    public int CompletedCount => _items.Count(t => t.IsCompleted);
    /// <summary>未完了アイテム数を返す。</summary>
    public int PendingCount   => _items.Count(t => !t.IsCompleted);
    /// <summary>期限切れアイテム数を返す。</summary>
    public int OverdueCount   => _items.Count(t => t.IsOverdue);

    // ── 永続化 ───────────────────────────────────────────
    /// <summary>データを JSON ファイルに保存して変更イベントを発火する。</summary>
    private void SaveAndNotify()
    {
        JsonFileStore.Save(FILE_NAME, _items);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }
}
